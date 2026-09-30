/*
    OPTIONAL. Copies employees, vendors and their roles from the existing QPS tables into auth.*.
    Run after 001_auth_store.sql, then on a schedule (SQL Agent) if users keep being maintained in
    the old screens.

    EDIT FIRST: every name marked  -- TODO  is a placeholder for your real table/column. Use the same
    "is this user allowed in" rule as the current login and Get_User_Details_On_User_Code.

    Behaviour
      - inserts new users, updates changed ones, deactivates users missing from the source
        (never deletes, so auth.UserSecurity history and FKs stay intact)
      - roles for synced users are replaced by the source mapping; unknown role names are skipped
        and listed at the end
      - passwords are NOT copied (legacy values are reversible encryption, not hashes)
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-------------------------------------------------------------------------------------------------
-- 1. Source users
-------------------------------------------------------------------------------------------------
DECLARE @Src TABLE
(
    AccountType NVARCHAR(20)  NOT NULL,
    UserCode    NVARCHAR(50)  NOT NULL,
    UserName    NVARCHAR(256) NOT NULL,
    Email       NVARCHAR(256) NULL,
    DisplayName NVARCHAR(200) NOT NULL,
    IsActive    BIT           NOT NULL,
    PRIMARY KEY (AccountType, UserCode)
);

INSERT INTO @Src (AccountType, UserCode, UserName, Email, DisplayName, IsActive)
SELECT N'Employee',
       CAST(e.USER_CODE AS NVARCHAR(50)),                                    -- TODO employee code
       CAST(e.USER_CODE AS NVARCHAR(256)),                                   -- TODO login id
       NULLIF(LTRIM(RTRIM(CAST(e.EMAIL_ID AS NVARCHAR(256)))), N''),         -- TODO
       CAST(ISNULL(e.USER_NAME, e.USER_CODE) AS NVARCHAR(200)),              -- TODO
       CAST(CASE WHEN e.IS_ACTIVE = 1 AND e.RESIGNATION_DATE IS NULL THEN 1 ELSE 0 END AS BIT) -- TODO
FROM dbo.USER_MASTER e                                                       -- TODO employee table
UNION ALL
SELECT N'Vendor',
       CAST(v.VENDOR_CODE AS NVARCHAR(50)),                                  -- TODO vendor code
       CAST(v.VENDOR_CODE AS NVARCHAR(256)),                                 -- TODO login id
       NULLIF(LTRIM(RTRIM(CAST(v.EMAIL_ID AS NVARCHAR(256)))), N''),         -- TODO
       CAST(ISNULL(v.VENDOR_NAME, v.VENDOR_CODE) AS NVARCHAR(200)),          -- TODO
       CAST(CASE WHEN v.IS_ACTIVE = 1 THEN 1 ELSE 0 END AS BIT)              -- TODO
FROM dbo.VENDOR_MASTER v;                                                    -- TODO vendor table

-- Duplicate e-mails would break UX_AppUser_Email (and make e-mail login ambiguous): drop the e-mail
-- on those rows so the user can still log in with their user name.
UPDATE s SET Email = NULL
FROM @Src s
WHERE s.Email IS NOT NULL
  AND (SELECT COUNT(*) FROM @Src d WHERE d.AccountType = s.AccountType AND d.Email = s.Email) > 1;

-------------------------------------------------------------------------------------------------
-- 2. Upsert users; deactivate those no longer in the source
-------------------------------------------------------------------------------------------------
DECLARE @Changes TABLE (Id NVARCHAR(60) NOT NULL, WasActive BIT NULL, IsActive BIT NOT NULL);

MERGE auth.AppUser WITH (HOLDLOCK) AS t
USING @Src AS s ON t.AccountType = s.AccountType AND t.UserCode = s.UserCode
WHEN MATCHED AND (t.UserName <> s.UserName OR ISNULL(t.Email, N'') <> ISNULL(s.Email, N'')
                  OR t.DisplayName <> s.DisplayName OR t.IsActive <> s.IsActive) THEN
    UPDATE SET UserName = s.UserName, Email = s.Email, DisplayName = s.DisplayName,
               IsActive = s.IsActive, UpdatedUtc = SYSDATETIMEOFFSET()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountType, UserCode, UserName, Email, DisplayName, IsActive)
    VALUES (s.AccountType, s.UserCode, s.UserName, s.Email, s.DisplayName, s.IsActive)
WHEN NOT MATCHED BY SOURCE AND t.IsActive = 1 THEN
    UPDATE SET IsActive = 0, UpdatedUtc = SYSDATETIMEOFFSET()
OUTPUT inserted.Id, deleted.IsActive, inserted.IsActive INTO @Changes (Id, WasActive, IsActive);

-- Users deactivated in this run (resigned in the source or removed from it): rotate the stamp so any open session is rejected on its next revalidation.
UPDATE sec SET SecurityStamp = REPLACE(CONVERT(NVARCHAR(36), NEWID()), N'-', N''), UpdatedUtc = SYSDATETIMEOFFSET()
FROM auth.UserSecurity sec
JOIN @Changes c ON c.Id = sec.UserId AND c.WasActive = 1 AND c.IsActive = 0;

-------------------------------------------------------------------------------------------------
-- 3. Roles
-------------------------------------------------------------------------------------------------
DECLARE @SrcRoles TABLE (UserId NVARCHAR(60) NOT NULL, RoleName NVARCHAR(100) NOT NULL, PRIMARY KEY (UserId, RoleName));

INSERT INTO @SrcRoles (UserId, RoleName)
SELECT DISTINCT N'E:' + CAST(ur.USER_CODE AS NVARCHAR(50)),                 -- TODO
                LTRIM(RTRIM(CAST(r.ROLE_NAME AS NVARCHAR(100))))             -- TODO; map to QpsRoles values here if they differ
FROM dbo.USER_ROLE_MAPPING ur                                                -- TODO
JOIN dbo.ROLE_MASTER r ON r.ROLE_ID = ur.ROLE_ID                             -- TODO
UNION
SELECT N'V:' + s.UserCode, N'Vendor' FROM @Src s WHERE s.AccountType = N'Vendor';

-- Only synced users with an existing role are touched; users created by hand in auth.* keep theirs.
DELETE ur
FROM auth.UserRole ur
JOIN auth.AppUser u ON u.Id = ur.UserId
JOIN @Src s ON s.AccountType = u.AccountType AND s.UserCode = u.UserCode
WHERE NOT EXISTS (SELECT 1 FROM @SrcRoles x WHERE x.UserId = ur.UserId AND x.RoleName = ur.RoleName);

INSERT INTO auth.UserRole (UserId, RoleName)
SELECT x.UserId, x.RoleName
FROM @SrcRoles x
JOIN auth.AppUser u ON u.Id = x.UserId
JOIN auth.Role r ON r.RoleName = x.RoleName
WHERE NOT EXISTS (SELECT 1 FROM auth.UserRole ur WHERE ur.UserId = x.UserId AND ur.RoleName = x.RoleName);

COMMIT;

-- Role names in the source that have no match in auth.Role (add them there or map them above).
SELECT DISTINCT x.RoleName AS UnknownRole
FROM @SrcRoles x
WHERE NOT EXISTS (SELECT 1 FROM auth.Role r WHERE r.RoleName = x.RoleName);
