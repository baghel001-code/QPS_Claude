/*
    Complete database schema for the Admin auth module (Admin.Auth.SqlAuthUserStore).

    Objects created (all in schema [auth]):

      Tables
        auth.AppUser             employees and vendors that can sign in
        auth.Role                role catalogue (names = QpsRoles constants)
        auth.UserRole            user <-> role
        auth.Module              module catalogue (QpsRoles.Module* constants)
        auth.UserModule          user <-> module
        auth.UserSecurity        password hash, security stamp, failed logins, lockout
        auth.PasswordResetToken  hashed, single-use password reset tokens

      Views (read by SqlAuthUserStore; keep their column names and types)
        auth.vw_AuthUser, auth.vw_AuthUserRole, auth.vw_AuthUserModule

      Procedures (for admin screens / jobs; the store itself does not call them)
        auth.usp_UpsertUser, auth.usp_SetUserRoles, auth.usp_SetUserModules,
        auth.usp_PurgeExpiredResetTokens

      Seed data
        every role and module used by the app

    Idempotent: safe to run more than once. Needs SQL Server 2016 SP1+.
    Related scripts:
      002_seed_test_users.sql      the same test users as InMemoryAuthUserStore (dev/test only)
      003_sync_from_legacy.sql     copy users/roles from the existing QPS tables into auth.*
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA auth');
GO

-------------------------------------------------------------------------------------------------
-- auth.AppUser
-- Id is what the app uses everywhere (claims, UserSecurity, tokens): "E:{UserCode}" / "V:{UserCode}".
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.AppUser', N'U') IS NULL
CREATE TABLE auth.AppUser
(
    AccountType  NVARCHAR(20)   NOT NULL,
    UserCode     NVARCHAR(50)   NOT NULL,   -- employee code / vendor code
    Id           AS CAST(CASE AccountType WHEN N'Employee' THEN N'E:' ELSE N'V:' END + UserCode AS NVARCHAR(60)) PERSISTED NOT NULL,
    UserName     NVARCHAR(256)  NOT NULL,   -- login id
    Email        NVARCHAR(256)  NULL,       -- can also be used to log in
    DisplayName  NVARCHAR(200)  NOT NULL,
    IsActive     BIT            NOT NULL CONSTRAINT DF_AppUser_IsActive DEFAULT (1),
    CreatedUtc   DATETIMEOFFSET NOT NULL CONSTRAINT DF_AppUser_CreatedUtc DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedUtc   DATETIMEOFFSET NOT NULL CONSTRAINT DF_AppUser_UpdatedUtc DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_AppUser PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_AppUser_Code UNIQUE (AccountType, UserCode),
    CONSTRAINT CK_AppUser_AccountType CHECK (AccountType IN (N'Employee', N'Vendor')),
    CONSTRAINT CK_AppUser_UserName CHECK (LEN(UserName) > 0),
    CONSTRAINT CK_AppUser_UserCode CHECK (LEN(UserCode) > 0)
);
GO

-- Login lookup: FindByLoginAsync filters on (AccountType, UserName) or (AccountType, Email).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_UserName' AND object_id = OBJECT_ID(N'auth.AppUser'))
    CREATE UNIQUE INDEX UX_AppUser_UserName ON auth.AppUser (AccountType, UserName);
GO
-- Unique so an e-mail login can never resolve to someone else's account.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AppUser_Email' AND object_id = OBJECT_ID(N'auth.AppUser'))
    CREATE UNIQUE INDEX UX_AppUser_Email ON auth.AppUser (AccountType, Email) WHERE Email IS NOT NULL;
GO

-------------------------------------------------------------------------------------------------
-- Roles
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.Role', N'U') IS NULL
CREATE TABLE auth.Role
(
    RoleName     NVARCHAR(100) NOT NULL CONSTRAINT PK_Role PRIMARY KEY, -- must equal the QpsRoles constant value
    Description  NVARCHAR(200) NULL,
    AccountType  NVARCHAR(20)  NULL,   -- NULL = any; otherwise only for Employee or Vendor accounts
    CONSTRAINT CK_Role_AccountType CHECK (AccountType IS NULL OR AccountType IN (N'Employee', N'Vendor'))
);
GO

IF OBJECT_ID(N'auth.UserRole', N'U') IS NULL
CREATE TABLE auth.UserRole
(
    UserId      NVARCHAR(60)   NOT NULL,
    RoleName    NVARCHAR(100)  NOT NULL,
    CreatedUtc  DATETIMEOFFSET NOT NULL CONSTRAINT DF_UserRole_CreatedUtc DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_UserRole PRIMARY KEY CLUSTERED (UserId, RoleName),
    CONSTRAINT FK_UserRole_User FOREIGN KEY (UserId) REFERENCES auth.AppUser (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleName) REFERENCES auth.Role (RoleName) ON UPDATE CASCADE
);
GO

-------------------------------------------------------------------------------------------------
-- Modules
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.Module', N'U') IS NULL
CREATE TABLE auth.Module
(
    ModuleName   NVARCHAR(100) NOT NULL CONSTRAINT PK_Module PRIMARY KEY, -- must equal the QpsRoles.Module* value
    Description  NVARCHAR(200) NULL
);
GO

IF OBJECT_ID(N'auth.UserModule', N'U') IS NULL
CREATE TABLE auth.UserModule
(
    UserId      NVARCHAR(60)   NOT NULL,
    ModuleName  NVARCHAR(100)  NOT NULL,
    CreatedUtc  DATETIMEOFFSET NOT NULL CONSTRAINT DF_UserModule_CreatedUtc DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT PK_UserModule PRIMARY KEY CLUSTERED (UserId, ModuleName),
    CONSTRAINT FK_UserModule_User FOREIGN KEY (UserId) REFERENCES auth.AppUser (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserModule_Module FOREIGN KEY (ModuleName) REFERENCES auth.Module (ModuleName) ON UPDATE CASCADE
);
GO

-------------------------------------------------------------------------------------------------
-- auth.UserSecurity  (written by SqlAuthUserStore; one row per user, created on first write)
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.UserSecurity', N'U') IS NULL
CREATE TABLE auth.UserSecurity
(
    UserId              NVARCHAR(60)   NOT NULL CONSTRAINT PK_UserSecurity PRIMARY KEY,
    PasswordHash        NVARCHAR(500)  NULL,          -- IPasswordHasher output; NULL = never set
    SecurityStamp       NVARCHAR(64)   NOT NULL,      -- rotated on password change -> signs out other sessions
    FailedLoginCount    INT            NOT NULL CONSTRAINT DF_UserSecurity_Failed DEFAULT (0),
    LockoutEndUtc       DATETIMEOFFSET NULL,
    MustChangePassword  BIT            NOT NULL CONSTRAINT DF_UserSecurity_MustChange DEFAULT (1),
    PasswordChangedUtc  DATETIMEOFFSET NULL,
    UpdatedUtc          DATETIMEOFFSET NOT NULL,
    CONSTRAINT CK_UserSecurity_Failed CHECK (FailedLoginCount >= 0)
);
GO
-- Added separately so databases where an earlier version of this script created the table get it too.
IF OBJECT_ID(N'FK_UserSecurity_User', N'F') IS NULL
    ALTER TABLE auth.UserSecurity ADD CONSTRAINT FK_UserSecurity_User
        FOREIGN KEY (UserId) REFERENCES auth.AppUser (Id) ON DELETE CASCADE;
GO

-------------------------------------------------------------------------------------------------
-- auth.PasswordResetToken  (written by SqlAuthUserStore)
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.PasswordResetToken', N'U') IS NULL
CREATE TABLE auth.PasswordResetToken
(
    TokenHash   NVARCHAR(128)  NOT NULL CONSTRAINT PK_PasswordResetToken PRIMARY KEY, -- hash only, never the raw token
    UserId      NVARCHAR(60)   NOT NULL,
    ExpiresUtc  DATETIMEOFFSET NOT NULL,
    CreatedUtc  DATETIMEOFFSET NOT NULL
);
GO
IF OBJECT_ID(N'FK_PasswordResetToken_User', N'F') IS NULL
    ALTER TABLE auth.PasswordResetToken ADD CONSTRAINT FK_PasswordResetToken_User
        FOREIGN KEY (UserId) REFERENCES auth.AppUser (Id) ON DELETE CASCADE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_UserId')
    CREATE INDEX IX_PasswordResetToken_UserId ON auth.PasswordResetToken (UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_ExpiresUtc')
    CREATE INDEX IX_PasswordResetToken_ExpiresUtc ON auth.PasswordResetToken (ExpiresUtc);
GO

-------------------------------------------------------------------------------------------------
-- Views read by SqlAuthUserStore. Column names/types are the contract with the C# code.
-------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW auth.vw_AuthUser
AS
    SELECT u.Id, u.UserName, u.Email, u.DisplayName, u.IsActive, u.AccountType
    FROM auth.AppUser u;
GO

-- Roles that don't apply to the account type (e.g. Vendor on an employee) are ignored.
CREATE OR ALTER VIEW auth.vw_AuthUserRole
AS
    SELECT ur.UserId, ur.RoleName
    FROM auth.UserRole ur
    JOIN auth.AppUser u ON u.Id = ur.UserId
    JOIN auth.Role r ON r.RoleName = ur.RoleName
    WHERE r.AccountType IS NULL OR r.AccountType = u.AccountType;
GO

CREATE OR ALTER VIEW auth.vw_AuthUserModule
AS
    SELECT um.UserId, um.ModuleName
    FROM auth.UserModule um;
GO

-------------------------------------------------------------------------------------------------
-- Procedures for user administration and housekeeping
-------------------------------------------------------------------------------------------------

-- Create or update a user. Returns the user's Id.
CREATE OR ALTER PROCEDURE auth.usp_UpsertUser
    @AccountType NVARCHAR(20),
    @UserCode    NVARCHAR(50),
    @UserName    NVARCHAR(256),
    @Email       NVARCHAR(256) = NULL,
    @DisplayName NVARCHAR(200),
    @IsActive    BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    MERGE auth.AppUser WITH (HOLDLOCK) AS t
    USING (SELECT @AccountType AS AccountType, @UserCode AS UserCode) AS s
        ON t.AccountType = s.AccountType AND t.UserCode = s.UserCode
    WHEN MATCHED THEN
        UPDATE SET UserName = @UserName, Email = NULLIF(LTRIM(RTRIM(@Email)), N''), DisplayName = @DisplayName,
                   IsActive = @IsActive, UpdatedUtc = SYSDATETIMEOFFSET()
    WHEN NOT MATCHED THEN
        INSERT (AccountType, UserCode, UserName, Email, DisplayName, IsActive)
        VALUES (@AccountType, @UserCode, @UserName, NULLIF(LTRIM(RTRIM(@Email)), N''), @DisplayName, @IsActive);

    -- Deactivating a user also rotates the stamp so open sessions fail revalidation.
    IF @IsActive = 0
        UPDATE s SET SecurityStamp = REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), UpdatedUtc = SYSDATETIMEOFFSET()
        FROM auth.UserSecurity s JOIN auth.AppUser u ON u.Id = s.UserId
        WHERE u.AccountType = @AccountType AND u.UserCode = @UserCode;

    SELECT Id FROM auth.AppUser WHERE AccountType = @AccountType AND UserCode = @UserCode;
END
GO

-- Replace a user's roles with a comma-separated list, e.g. N'QAM,QAAdmin'.
CREATE OR ALTER PROCEDURE auth.usp_SetUserRoles
    @UserId NVARCHAR(60),
    @Roles  NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DELETE FROM auth.UserRole WHERE UserId = @UserId;
    INSERT INTO auth.UserRole (UserId, RoleName)
    SELECT DISTINCT @UserId, LTRIM(RTRIM(value))
    FROM STRING_SPLIT(@Roles, N',')
    WHERE LTRIM(RTRIM(value)) <> N'';   -- unknown role names fail on FK_UserRole_Role

    COMMIT;
END
GO

-- Replace a user's modules with a comma-separated list.
CREATE OR ALTER PROCEDURE auth.usp_SetUserModules
    @UserId  NVARCHAR(60),
    @Modules NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    DELETE FROM auth.UserModule WHERE UserId = @UserId;
    INSERT INTO auth.UserModule (UserId, ModuleName)
    SELECT DISTINCT @UserId, LTRIM(RTRIM(value))
    FROM STRING_SPLIT(@Modules, N',')
    WHERE LTRIM(RTRIM(value)) <> N'';

    COMMIT;
END
GO

-- Schedule daily (SQL Agent). The store also prunes expired tokens whenever it issues one.
CREATE OR ALTER PROCEDURE auth.usp_PurgeExpiredResetTokens
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM auth.PasswordResetToken WHERE ExpiresUtc <= SYSDATETIMEOFFSET();
    SELECT @@ROWCOUNT AS DeletedTokens;
END
GO

-------------------------------------------------------------------------------------------------
-- Seed: roles and modules.
-- RoleName / ModuleName must equal the string VALUES of the QpsRoles constants. The values below
-- are the constant names; if a constant's value differs (e.g. CategoryHead = "CH"), change it here.
-------------------------------------------------------------------------------------------------
MERGE auth.Role AS t
USING (VALUES
    (N'Administrator',         N'Full system administrator',    N'Employee'),
    (N'Admin',                 N'Administrator',                N'Employee'),
    (N'QAAdmin',               N'QA administrator',             N'Employee'),
    (N'QAM',                   N'QA manager',                   N'Employee'),
    (N'QA',                    N'Quality assurance',            N'Employee'),
    (N'CategoryHead',          N'Category head',                N'Employee'),
    (N'AssociateCategoryHead', N'Associate category head',      N'Employee'),
    (N'Merchandiser',          N'Merchandiser',                 N'Employee'),
    (N'Buyer',                 N'Buyer',                        N'Employee'),
    (N'QC',                    N'Quality control',              N'Employee'),
    (N'View',                  N'Read-only access',             N'Employee'),
    (N'ASN',                   N'Advance shipping notice',      N'Employee'),
    (N'BFT',                   N'BFT',                          N'Employee'),
    (N'CM',                    N'CM',                           N'Employee'),
    (N'Vendor',                N'Vendor portal user',           N'Vendor')
) AS s (RoleName, Description, AccountType)
    ON t.RoleName = s.RoleName
WHEN MATCHED THEN UPDATE SET Description = s.Description, AccountType = s.AccountType
WHEN NOT MATCHED THEN INSERT (RoleName, Description, AccountType) VALUES (s.RoleName, s.Description, s.AccountType);
GO

MERGE auth.Module AS t
USING (VALUES
    (N'UserManagement', N'User management screens'),
    (N'Menu',           N'Menu configuration')
) AS s (ModuleName, Description)
    ON t.ModuleName = s.ModuleName
WHEN MATCHED THEN UPDATE SET Description = s.Description
WHEN NOT MATCHED THEN INSERT (ModuleName, Description) VALUES (s.ModuleName, s.Description);
GO
