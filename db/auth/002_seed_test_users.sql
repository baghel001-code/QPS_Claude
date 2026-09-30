/*
    DEV / TEST ONLY. Creates the same users as InMemoryAuthUserStore, with the same roles and modules.
    Run after 001_auth_store.sql. Do not run in production.

    Passwords are not seeded: IPasswordHasher hashes can't be produced in SQL. Each user gets
    MustChangePassword = 1 and no hash, so set one through "Forgot password" or with
    SqlAuthUserStore.SetPasswordHashAsync(userId, hasher.HashPassword("ChangeMe!2026"), true).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Users TABLE (AccountType NVARCHAR(20), UserCode NVARCHAR(50), UserName NVARCHAR(256), DisplayName NVARCHAR(200), Roles NVARCHAR(400), Modules NVARCHAR(400));
INSERT INTO @Users VALUES
    (N'Employee', N'1',  N'admin',   N'Rohit Patel',   N'Administrator',         N'UserManagement,Menu'),
    (N'Employee', N'2',  N'admin5',  N'Meera Iyer',    N'Admin',                 N''),
    (N'Employee', N'3',  N'qaadmin', N'Neha Gupta',    N'QAAdmin',               N''),
    (N'Employee', N'4',  N'qam',     N'Amit Sharma',   N'QAM',                   N''),
    (N'Employee', N'5',  N'qa',      N'Priya Rao',     N'QA',                    N''),
    (N'Employee', N'6',  N'ch',      N'Rakesh Verma',  N'CategoryHead',          N''),
    (N'Employee', N'7',  N'ach',     N'Pooja Nair',    N'AssociateCategoryHead', N''),
    (N'Employee', N'8',  N'merch',   N'Karan Mehta',   N'Merchandiser',          N''),
    (N'Employee', N'9',  N'buyer',   N'Sana Ali',      N'Buyer',                 N''),
    (N'Employee', N'10', N'qc',      N'Vivek Singh',   N'QC',                    N''),
    (N'Employee', N'11', N'view',    N'Anita Das',     N'View',                  N''),
    (N'Employee', N'12', N'asn',     N'Manoj Kumar',   N'ASN',                   N''),
    (N'Employee', N'13', N'bft',     N'Deepak Joshi',  N'BFT',                   N''),
    (N'Employee', N'14', N'cm',      N'Ritu Malhotra', N'CM',                    N''),
    (N'Employee', N'15', N'multi',   N'Sanjay Rao',    N'QAM,QAAdmin',           N''),
    (N'Employee', N'16', N'staff',   N'Arjun Menon',   N'',                      N''),
    (N'Vendor',   N'1',  N'vendor1', N'Sample Vendor Pvt Ltd', N'Vendor',        N''),
    (N'Vendor',   N'2',  N'vendor2', N'Kaveri Knitwear',       N'Vendor',        N'');

DECLARE @AccountType NVARCHAR(20), @UserCode NVARCHAR(50), @UserName NVARCHAR(256), @DisplayName NVARCHAR(200),
        @Roles NVARCHAR(400), @Modules NVARCHAR(400), @Email NVARCHAR(256), @Id NVARCHAR(60);
DECLARE @Ids TABLE (Id NVARCHAR(60));

DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT AccountType, UserCode, UserName, DisplayName, Roles, Modules FROM @Users;
OPEN c;
FETCH NEXT FROM c INTO @AccountType, @UserCode, @UserName, @DisplayName, @Roles, @Modules;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Email = @UserName + N'@example.com';
    DELETE FROM @Ids;
    INSERT INTO @Ids EXEC auth.usp_UpsertUser @AccountType, @UserCode, @UserName, @Email, @DisplayName, 1;
    SELECT @Id = Id FROM @Ids;

    EXEC auth.usp_SetUserRoles @Id, @Roles;
    EXEC auth.usp_SetUserModules @Id, @Modules;

    FETCH NEXT FROM c INTO @AccountType, @UserCode, @UserName, @DisplayName, @Roles, @Modules;
END
CLOSE c;
DEALLOCATE c;

COMMIT;

SELECT u.Id, u.UserName, u.DisplayName, r.RoleName
FROM auth.AppUser u LEFT JOIN auth.UserRole r ON r.UserId = u.Id
ORDER BY u.AccountType, u.UserCode, r.RoleName;
