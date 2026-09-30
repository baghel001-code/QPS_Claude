# Real user store (`SqlAuthUserStore`)

`InMemoryAuthUserStore` only holds seeded test users. `Admin/Auth/SqlAuthUserStore.cs` implements
the same `IAuthUserStore` against SQL Server, reading the real employee and vendor records that the
existing login pages use.

## Database objects (`db/auth/`)

| Script | What it does |
|---|---|
| `001_auth_store.sql` | Creates every object in schema `auth` (listed below) and seeds all roles and modules. Safe to run more than once. |
| `002_seed_test_users.sql` | **Dev/test only.** Creates the same 18 test users as `InMemoryAuthUserStore`, with their roles and modules. Passwords aren't seeded. |
| `003_sync_from_legacy.sql` | Optional. Copies employees, vendors and roles from the existing QPS tables into `auth.*`. Edit its `-- TODO` names first. You can run it on a schedule. |

| Object | Kind | Used by |
|---|---|---|
| `auth.AppUser` | table: employees and vendors. `Id` = `E:{code}` / `V:{code}` (computed) | views |
| `auth.Role`, `auth.UserRole` | tables: role catalogue and user-role mapping | `vw_AuthUserRole` |
| `auth.Module`, `auth.UserModule` | tables: module catalogue and user-module mapping | `vw_AuthUserModule` |
| `auth.UserSecurity` | table: password hash, security stamp, failed logins, lockout | `SqlAuthUserStore` (read/write) |
| `auth.PasswordResetToken` | table: hashed, single-use reset tokens | `SqlAuthUserStore` (read/write) |
| `auth.vw_AuthUser`, `auth.vw_AuthUserRole`, `auth.vw_AuthUserModule` | views: the contract with the C# code | `SqlAuthUserStore` (read) |
| `auth.usp_UpsertUser` | proc: create or update a user (rotates the security stamp when a user is deactivated) | admin screens, scripts |
| `auth.usp_SetUserRoles`, `auth.usp_SetUserModules` | procs: replace roles/modules from a comma-separated list | admin screens, scripts |
| `auth.usp_PurgeExpiredResetTokens` | proc: delete expired tokens | SQL Agent job (daily) |

The seeded role and module names are the `QpsRoles` constant **names**, for example `CategoryHead`.
If a constant's **value** is different, for example `CategoryHead = "CH"`, change the seed in
`001` so the names match the values.

## Setup

1. Run `001_auth_store.sql`. Then either run `003_sync_from_legacy.sql` (after editing its `-- TODO`
   names) to bring in your real users, or create users with `auth.usp_UpsertUser` and
   `auth.usp_SetUserRoles`. For a test database, run `002_seed_test_users.sql` instead.
2. Add the connection string to `appsettings.json`:
   ```json
   "ConnectionStrings": { "QpsAuth": "Server=...;Database=...;..." }
   ```
3. `Program.cs` already calls `AddQpsAuthUserStore(...)`. Delete any other `IAuthUserStore`
   registration, for example one in `AddDependencies()`.
4. The Admin project needs a reference to `Microsoft.Data.SqlClient`. EF Core's SQL Server provider
   already brings it in. If the project doesn't get it that way, add
   `<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />`.

To keep using the in-memory test users locally, set `"Auth": { "UseInMemoryStore": true }` in
`appsettings.Development.json`. This setting is ignored outside Development.

## Existing users' passwords

The old login stores passwords with reversible encryption (`New_Enc_Dec` / `PasswordEncryptConverter`).
Those values are not `IPasswordHasher` hashes. A user with no row in `auth.UserSecurity` has an
empty hash and `MustChangePassword = true`, so they cannot sign in until they set a password. You can
handle this in one of two ways:

- **Self-service**: users set a password through "Forgot password". The reset flow calls
  `SetPasswordHashAsync`, which creates the row and clears `MustChangePassword`.
- **One-off migration**: decrypt each legacy password with `New_Enc_Dec`, run it through
  `IPasswordHasher.HashPassword`, and insert it into `auth.UserSecurity`. Then remove the reversible
  copies.

## Behaviour notes

- `RecordFailedLoginAsync` increments the count in SQL, so concurrent failed logins cannot lose
  counts.
- The first write for a user creates their `auth.UserSecurity` row with a `MERGE ... WITH (HOLDLOCK)`.
- `ConsumePasswordResetTokenAsync` uses `DELETE ... OUTPUT`, so a reset link works only once, even
  when two requests race.
- `SetPasswordHashAsync` also clears `MustChangePassword`.
- The store is a stateless singleton. Each call opens a pooled connection.
