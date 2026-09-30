using Application.Interfaces.V1;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Admin.Auth;

/// <summary>
/// Production <see cref="IAuthUserStore"/> backed by SQL Server.
///
/// Users, roles and modules are read from the views in <c>db/auth/001_auth_store.sql</c>
/// (<c>auth.vw_AuthUser</c>, <c>auth.vw_AuthUserRole</c>, <c>auth.vw_AuthUserModule</c>), which sit on
/// top of the same employee / vendor tables the existing login pages use. Everything the old login
/// never stored (password hash, security stamp, lockout, reset tokens) lives in <c>auth.UserSecurity</c>
/// and <c>auth.PasswordResetToken</c>, keyed by the same <c>Id</c> ("E:{code}" / "V:{code}").
/// </summary>
public sealed class SqlAuthUserStore : IAuthUserStore
{
    private readonly string _connectionString;
    private readonly TimeProvider _clock;

    public SqlAuthUserStore(IConfiguration configuration, TimeProvider clock)
    {
        _connectionString = configuration.GetConnectionString(AuthStoreSetup.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{AuthStoreSetup.ConnectionStringName}' is missing (ConnectionStrings section of appsettings.json).");
        _clock = clock;
    }

    public async Task<AuthUser?> FindByLoginAsync(string login, AccountType type, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login)) return null;

        // User name wins over e-mail when both match different rows.
        const string sql = """
            SELECT TOP (1) u.Id FROM auth.vw_AuthUser u
            WHERE u.AccountType = @AccountType AND (u.UserName = @Login OR u.Email = @Login)
            ORDER BY CASE WHEN u.UserName = @Login THEN 0 ELSE 1 END;
            """;
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, sql);
        cmd.Parameters.Add("@AccountType", SqlDbType.NVarChar, 20).Value = type.ToString();
        cmd.Parameters.Add("@Login", SqlDbType.NVarChar, 256).Value = login.Trim();
        var id = await cmd.ExecuteScalarAsync(ct) as string;
        return id is null ? null : await LoadAsync(conn, id, ct);
    }

    public async Task<AuthUser?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        await using var conn = await OpenAsync(ct);
        return await LoadAsync(conn, userId, ct);
    }

    public async Task<int> RecordFailedLoginAsync(string userId, CancellationToken ct = default)
    {
        // Increment in SQL (not read-modify-write) so concurrent failed logins cannot lose a count.
        const string sql = EnsureSecurityRow + """
            UPDATE auth.UserSecurity SET FailedLoginCount = FailedLoginCount + 1, UpdatedUtc = @NowUtc
            OUTPUT inserted.FailedLoginCount
            WHERE UserId = @UserId;
            """;
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, sql);
        AddUserId(cmd, userId);
        AddNow(cmd);
        return await cmd.ExecuteScalarAsync(ct) is int count ? count : 0;
    }

    public Task LockOutAsync(string userId, DateTimeOffset untilUtc, CancellationToken ct = default) =>
        ExecuteAsync(EnsureSecurityRow + """
            UPDATE auth.UserSecurity SET FailedLoginCount = 0, LockoutEndUtc = @LockoutEndUtc, UpdatedUtc = @NowUtc
            WHERE UserId = @UserId;
            """, userId, cmd => cmd.Parameters.Add("@LockoutEndUtc", SqlDbType.DateTimeOffset).Value = untilUtc, ct);

    public Task ClearLockoutAsync(string userId, CancellationToken ct = default) =>
        ExecuteAsync("""
            UPDATE auth.UserSecurity SET FailedLoginCount = 0, LockoutEndUtc = NULL, UpdatedUtc = @NowUtc
            WHERE UserId = @UserId;
            """, userId, null, ct);

    public Task SetPasswordHashAsync(string userId, string passwordHash, bool rotateSecurityStamp, CancellationToken ct = default) =>
        ExecuteAsync(EnsureSecurityRow + """
            UPDATE auth.UserSecurity
            SET PasswordHash = @PasswordHash,
                SecurityStamp = CASE WHEN @Rotate = 1 THEN @NewStamp ELSE SecurityStamp END,
                MustChangePassword = 0,
                PasswordChangedUtc = @NowUtc,
                UpdatedUtc = @NowUtc
            WHERE UserId = @UserId;
            """, userId, cmd =>
            {
                cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 500).Value = passwordHash;
                cmd.Parameters.Add("@Rotate", SqlDbType.Bit).Value = rotateSecurityStamp;
            }, ct);

    public async Task SavePasswordResetTokenAsync(string userId, string tokenHash, DateTimeOffset expiresUtc, CancellationToken ct = default)
    {
        // Only one live token per user: issuing a new one invalidates the previous link.
        const string sql = """
            DELETE FROM auth.PasswordResetToken WHERE UserId = @UserId OR ExpiresUtc <= @NowUtc;
            INSERT INTO auth.PasswordResetToken (TokenHash, UserId, ExpiresUtc, CreatedUtc)
            VALUES (@TokenHash, @UserId, @ExpiresUtc, @NowUtc);
            """;
        await using var conn = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        await using var cmd = Command(conn, sql, tx);
        AddUserId(cmd, userId);
        AddNow(cmd);
        AddTokenHash(cmd, tokenHash);
        cmd.Parameters.Add("@ExpiresUtc", SqlDbType.DateTimeOffset).Value = expiresUtc;
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<string?> FindUserIdByPasswordResetTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        const string sql = "SELECT UserId FROM auth.PasswordResetToken WHERE TokenHash = @TokenHash AND ExpiresUtc > @NowUtc;";
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, sql);
        AddTokenHash(cmd, tokenHash);
        AddNow(cmd);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<string?> ConsumePasswordResetTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        // DELETE ... OUTPUT is atomic: two requests racing with the same link can't both succeed.
        // Expired tokens are deleted too but return no user.
        const string sql = """
            DELETE FROM auth.PasswordResetToken
            OUTPUT deleted.UserId, deleted.ExpiresUtc
            WHERE TokenHash = @TokenHash;
            """;
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, sql);
        AddTokenHash(cmd, tokenHash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var userId = reader.GetString(0);
        var expiresUtc = reader.GetDateTimeOffset(1);
        return expiresUtc > _clock.GetUtcNow() ? userId : null;
    }

    // Users that existed before this store have no auth.UserSecurity row yet; create it on first write.
    // A single MERGE WITH (HOLDLOCK) so two concurrent first writes can't both insert.
    private const string EnsureSecurityRow = """
        MERGE auth.UserSecurity WITH (HOLDLOCK) AS t
        USING (SELECT @UserId AS UserId) AS s ON t.UserId = s.UserId
        WHEN NOT MATCHED THEN INSERT (UserId, SecurityStamp, UpdatedUtc) VALUES (@UserId, @NewStamp, @NowUtc);

        """;

    private async Task<AuthUser?> LoadAsync(SqlConnection conn, string userId, CancellationToken ct)
    {
        // One round trip: user + security row, then roles, then modules.
        const string sql = """
            SELECT u.Id, u.UserName, u.Email, u.DisplayName, u.IsActive, u.AccountType,
                   s.PasswordHash, s.SecurityStamp, s.FailedLoginCount, s.LockoutEndUtc, s.MustChangePassword
            FROM auth.vw_AuthUser u
            LEFT JOIN auth.UserSecurity s ON s.UserId = u.Id
            WHERE u.Id = @UserId;

            SELECT DISTINCT r.RoleName FROM auth.vw_AuthUserRole r WHERE r.UserId = @UserId;
            SELECT DISTINCT m.ModuleName FROM auth.vw_AuthUserModule m WHERE m.UserId = @UserId;
            """;
        await using var cmd = Command(conn, sql);
        AddUserId(cmd, userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var id = reader.GetString(0);
        var userName = reader.GetString(1);
        var email = reader.IsDBNull(2) ? "" : reader.GetString(2);
        var displayName = reader.IsDBNull(3) ? userName : reader.GetString(3);
        var isActive = reader.GetBoolean(4);
        var accountType = Enum.Parse<AccountType>(reader.GetString(5), ignoreCase: true);
        var passwordHash = reader.IsDBNull(6) ? "" : reader.GetString(6);
        // No security row yet: empty stamp until the first write creates one.
        var securityStamp = reader.IsDBNull(7) ? "" : reader.GetString(7);
        var failedLoginCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
        DateTimeOffset? lockoutEndUtc = reader.IsDBNull(9) ? null : reader.GetDateTimeOffset(9);
        // No hash yet means the user has never set a password in this store, so force a change.
        var mustChangePassword = reader.IsDBNull(10) ? passwordHash.Length == 0 : reader.GetBoolean(10);

        var roles = await ReadStringsAsync(reader, ct);
        var modules = await ReadStringsAsync(reader, ct);

        return new AuthUser(id, userName, email, displayName, passwordHash, securityStamp, IsActive: isActive,
            FailedLoginCount: failedLoginCount, LockoutEndUtc: lockoutEndUtc, Roles: roles, accountType)
        {
            Modules = modules,
            MustChangePassword = mustChangePassword,
        };
    }

    private static async Task<string[]> ReadStringsAsync(SqlDataReader reader, CancellationToken ct)
    {
        var values = new List<string>();
        if (!await reader.NextResultAsync(ct)) return [];
        while (await reader.ReadAsync(ct))
            if (!reader.IsDBNull(0)) values.Add(reader.GetString(0));
        return [.. values];
    }

    private async Task ExecuteAsync(string sql, string userId, Action<SqlCommand>? addParameters, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct);
        await using var cmd = Command(conn, sql);
        AddUserId(cmd, userId);
        AddNow(cmd);
        addParameters?.Invoke(cmd);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private static SqlCommand Command(SqlConnection conn, string sql, SqlTransaction? tx = null) =>
        new(sql, conn, tx) { CommandType = CommandType.Text, CommandTimeout = 30 };

    private static void AddUserId(SqlCommand cmd, string userId) =>
        cmd.Parameters.Add("@UserId", SqlDbType.NVarChar, 60).Value = userId;

    private static void AddTokenHash(SqlCommand cmd, string tokenHash) =>
        cmd.Parameters.Add("@TokenHash", SqlDbType.NVarChar, 128).Value = tokenHash;

    private void AddNow(SqlCommand cmd)
    {
        cmd.Parameters.Add("@NowUtc", SqlDbType.DateTimeOffset).Value = _clock.GetUtcNow();
        cmd.Parameters.Add("@NewStamp", SqlDbType.NVarChar, 64).Value = Guid.NewGuid().ToString("N");
    }
}
