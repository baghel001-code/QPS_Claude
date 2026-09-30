# Real user store (`SqlAuthUserStore`)

`InMemoryAuthUserStore` only holds seeded test users. `Admin/Auth/SqlAuthUserStore.cs` implements
the same `IAuthUserStore` against SQL Server, reading the real employee and vendor records that the
existing login pages use.

## Where data comes from

| Data | Source |
|---|---|
| User, e-mail, display name, active flag | `auth.vw_AuthUser`, a view over your existing employee and vendor tables |
| Roles | `auth.vw_AuthUserRole`. Names must match the `QpsRoles` constants |
| Modules | `auth.vw_AuthUserModule` (can be empty) |
| Password hash, security stamp, failed-login count, lockout | new table `auth.UserSecurity` |
| Password reset tokens (stored hashed) | new table `auth.PasswordResetToken` |

User ids keep the in-memory format: `E:{employee code}` and `V:{vendor code}`.

## Setup

1. Edit the `-- TODO` table and column names in `db/auth/001_auth_store.sql` to match your schema.
   Copy the "is this user allowed in" rules from the current login and from
   `Get_User_Details_On_User_Code` into `vw_AuthUser.IsActive`. Then run the script.
2. Add the connection string to `appsettings.json`:
   ```json
   "ConnectionStrings": { "QpsAuth": "Server=...;Database=...;..." }
   ```
3. `Program.cs` already calls `AddQpsAuthUserStore(...)`. Delete any other `IAuthUserStore`
   registration, for example one in `AddDependencies()`.
4. The Admin project needs a reference to `Microsoft.Data.SqlClient`. EF Core's SQL Server provider
   already brings it in. If the project doesn't get it that way, add
   `<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />`.

To keep using the seeded test users locally, set `"Auth": { "UseInMemoryStore": true }` in
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
