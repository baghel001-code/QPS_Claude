/*
    Schema for Admin.Auth.SqlAuthUserStore.

    1. Tables (run as-is): data the old login never stored — password hash, security stamp,
       lockout and reset tokens.
    2. Views (EDIT FIRST): map your existing employee / vendor / role / menu tables to the columns
       the store reads. Every name marked  -- TODO  is a placeholder for your real table/column.

    Idempotent: safe to run more than once. Needs SQL Server 2016 SP1+ (CREATE OR ALTER).
*/

IF SCHEMA_ID(N'auth') IS NULL EXEC(N'CREATE SCHEMA auth');
GO

-------------------------------------------------------------------------------------------------
-- 1. Tables
-------------------------------------------------------------------------------------------------
IF OBJECT_ID(N'auth.UserSecurity', N'U') IS NULL
CREATE TABLE auth.UserSecurity
(
    UserId              NVARCHAR(60)   NOT NULL CONSTRAINT PK_UserSecurity PRIMARY KEY, -- "E:{code}" / "V:{code}"
    PasswordHash        NVARCHAR(500)  NULL,          -- IPasswordHasher output; NULL = never set
    SecurityStamp       NVARCHAR(64)   NOT NULL,      -- rotated on password change -> signs out other sessions
    FailedLoginCount    INT            NOT NULL CONSTRAINT DF_UserSecurity_Failed DEFAULT (0),
    LockoutEndUtc       DATETIMEOFFSET NULL,
    MustChangePassword  BIT            NOT NULL CONSTRAINT DF_UserSecurity_MustChange DEFAULT (1),
    PasswordChangedUtc  DATETIMEOFFSET NULL,
    UpdatedUtc          DATETIMEOFFSET NOT NULL
);
GO

IF OBJECT_ID(N'auth.PasswordResetToken', N'U') IS NULL
CREATE TABLE auth.PasswordResetToken
(
    TokenHash   NVARCHAR(128)  NOT NULL CONSTRAINT PK_PasswordResetToken PRIMARY KEY, -- hash only, never the raw token
    UserId      NVARCHAR(60)   NOT NULL,
    ExpiresUtc  DATETIMEOFFSET NOT NULL,
    CreatedUtc  DATETIMEOFFSET NOT NULL
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PasswordResetToken_UserId')
    CREATE INDEX IX_PasswordResetToken_UserId ON auth.PasswordResetToken (UserId);
GO

-------------------------------------------------------------------------------------------------
-- 2. Views over the existing QPS user data  (EDIT the TODO names)
--
--    Contract the C# store relies on:
--      vw_AuthUser       Id NVARCHAR(60) unique, UserName, Email (nullable), DisplayName (nullable),
--                        IsActive BIT, AccountType 'Employee' | 'Vendor'
--      vw_AuthUserRole   UserId, RoleName   -- must match the QpsRoles constants exactly
--      vw_AuthUserModule UserId, ModuleName -- e.g. QpsRoles.ModuleUserManagement; may return no rows
--
--    Put the same "is this user allowed in" rules here that the current login page and
--    Get_User_Details_On_User_Code use (active, not resigned, vendor not blocked, ...).
--    UserName and Email lookups need an index on the underlying columns.
-------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW auth.vw_AuthUser
AS
    SELECT
        CAST(N'E:' + CAST(e.USER_CODE AS NVARCHAR(50)) AS NVARCHAR(60))    AS Id,          -- TODO employee key
        CAST(e.USER_CODE AS NVARCHAR(256))                                 AS UserName,    -- TODO login id
        CAST(e.EMAIL_ID AS NVARCHAR(256))                                  AS Email,       -- TODO
        CAST(e.USER_NAME AS NVARCHAR(200))                                 AS DisplayName, -- TODO
        CAST(CASE WHEN e.IS_ACTIVE = 1 AND e.RESIGNATION_DATE IS NULL
                  THEN 1 ELSE 0 END AS BIT)                                AS IsActive,    -- TODO same rule as today
        CAST(N'Employee' AS NVARCHAR(20))                                  AS AccountType
    FROM dbo.USER_MASTER e                                                                  -- TODO employee table

    UNION ALL

    SELECT
        CAST(N'V:' + CAST(v.VENDOR_CODE AS NVARCHAR(50)) AS NVARCHAR(60)),                   -- TODO vendor key
        CAST(v.VENDOR_CODE AS NVARCHAR(256)),                                                -- TODO login id
        CAST(v.EMAIL_ID AS NVARCHAR(256)),                                                   -- TODO
        CAST(v.VENDOR_NAME AS NVARCHAR(200)),                                                -- TODO
        CAST(CASE WHEN v.IS_ACTIVE = 1 THEN 1 ELSE 0 END AS BIT),                            -- TODO
        CAST(N'Vendor' AS NVARCHAR(20))
    FROM dbo.VENDOR_MASTER v;                                                                -- TODO vendor table
GO

CREATE OR ALTER VIEW auth.vw_AuthUserRole
AS
    SELECT CAST(N'E:' + CAST(ur.USER_CODE AS NVARCHAR(50)) AS NVARCHAR(60)) AS UserId,     -- TODO
           CAST(r.ROLE_NAME AS NVARCHAR(100))                               AS RoleName    -- TODO
    FROM dbo.USER_ROLE_MAPPING ur                                                           -- TODO
    JOIN dbo.ROLE_MASTER r ON r.ROLE_ID = ur.ROLE_ID                                        -- TODO

    UNION ALL

    -- Every vendor gets the Vendor role (QpsRoles.Vendor).
    SELECT CAST(N'V:' + CAST(v.VENDOR_CODE AS NVARCHAR(50)) AS NVARCHAR(60)), N'Vendor'   -- TODO
    FROM dbo.VENDOR_MASTER v;                                                                -- TODO
GO

CREATE OR ALTER VIEW auth.vw_AuthUserModule
AS
    SELECT CAST(N'E:' + CAST(um.USER_CODE AS NVARCHAR(50)) AS NVARCHAR(60)) AS UserId,     -- TODO
           CAST(um.MODULE_NAME AS NVARCHAR(100))                            AS ModuleName  -- TODO
    FROM dbo.USER_MODULE_MAPPING um;                                                        -- TODO; or
    -- SELECT CAST(NULL AS NVARCHAR(60)) AS UserId, CAST(NULL AS NVARCHAR(100)) AS ModuleName WHERE 1 = 0;
GO

-------------------------------------------------------------------------------------------------
-- Optional: periodic cleanup of expired reset tokens (the store also prunes them on each issue).
-- DELETE FROM auth.PasswordResetToken WHERE ExpiresUtc <= SYSDATETIMEOFFSET();
-------------------------------------------------------------------------------------------------
