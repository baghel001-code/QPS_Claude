/* =====================================================================================
   QPS access control (RBAC + ABAC) — tables for IAccessStore
   Permission = <Resource>.<Action>, e.g. 'Inspection.Ongoing.Edit'
   Actions: View, Create, Edit, Approve, Export
   Resources and permissions are written by the app at startup (AccessCatalogSync → SyncAsync).
   Role grants and menu are seeded once from AccessCatalog.Seed when the tables are empty,
   then managed in the User Management screens.
   ===================================================================================== */

IF SCHEMA_ID('auth') IS NULL EXEC('CREATE SCHEMA auth');
GO

CREATE TABLE auth.AccessResource (
    Code        NVARCHAR(100) NOT NULL PRIMARY KEY,      -- 'Inspection.Ongoing' (never renamed)
    Module      NVARCHAR(100) NOT NULL,                  -- 'Inspection Requests' (groups the matrix)
    Name        NVARCHAR(200) NOT NULL,
    Href        NVARCHAR(300) NULL,
    IsActive    BIT NOT NULL DEFAULT 1
);

CREATE TABLE auth.Permission (
    Code         NVARCHAR(120) NOT NULL PRIMARY KEY,     -- 'Inspection.Ongoing.Edit'
    ResourceCode NVARCHAR(100) NOT NULL REFERENCES auth.AccessResource(Code),
    Action       NVARCHAR(20)  NOT NULL CHECK (Action IN ('View','Create','Edit','Approve','Export'))
);

/* Roles: keep the existing role IDs ('1','2',…,'1014') as RoleKey; vendors use 'Vendor';
   module flags use 'module:User_Management' and 'module:Menu'. */
CREATE TABLE auth.RolePermission (
    RoleKey        NVARCHAR(50)  NOT NULL,
    PermissionCode NVARCHAR(120) NOT NULL REFERENCES auth.Permission(Code),
    GrantedBy      NVARCHAR(64)  NULL,
    GrantedOnUtc   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    PRIMARY KEY (RoleKey, PermissionCode)
);

/* Exceptions for one user. UserId = AuthUser.Id ('E:123', 'V:88'). Deny always wins. */
CREATE TABLE auth.UserPermission (
    UserId         NVARCHAR(64)  NOT NULL,
    PermissionCode NVARCHAR(120) NOT NULL REFERENCES auth.Permission(Code),
    Effect         NVARCHAR(5)   NOT NULL CHECK (Effect IN ('Grant','Deny')),
    Reason         NVARCHAR(400) NULL,
    ValidToUtc     DATETIME2     NULL,                   -- optional end date for temporary access
    PRIMARY KEY (UserId, PermissionCode)
);

/* ABAC values: which data a user's permissions apply to. */
CREATE TABLE auth.UserScope (
    UserId     NVARCHAR(64)  NOT NULL,
    ScopeType  NVARCHAR(20)  NOT NULL CHECK (ScopeType IN ('Region','Category','DC','Vendor')),
    ScopeValue NVARCHAR(100) NOT NULL,
    PRIMARY KEY (UserId, ScopeType, ScopeValue)
);

/* Left menu. Permission: one code, several separated by commas (all needed), or NULL. */
CREATE TABLE auth.Menu (
    Id         INT IDENTITY PRIMARY KEY,
    ParentId   INT NULL REFERENCES auth.Menu(Id),
    Title      NVARCHAR(100) NOT NULL,
    Href       NVARCHAR(300) NULL,                       -- NULL = group
    Icon       NVARCHAR(MAX) NULL,                       -- MudBlazor icon (SVG string)
    SortOrder  INT NOT NULL DEFAULT 0,
    Permission NVARCHAR(500) NULL,
    IsActive   BIT NOT NULL DEFAULT 1
);

/* Every change made in User Management. */
CREATE TABLE auth.AccessAudit (
    Id          BIGINT IDENTITY PRIMARY KEY,
    WhenUtc     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ChangedBy   NVARCHAR(64)  NOT NULL,
    Action      NVARCHAR(50)  NOT NULL,                  -- 'GrantRolePermission', 'AddUserRole', …
    Target      NVARCHAR(200) NOT NULL,                  -- role key / user id / menu id
    OldValue    NVARCHAR(MAX) NULL,
    NewValue    NVARCHAR(MAX) NULL
);
GO

/* ---------------- Queries behind IAccessStore ---------------- */

-- GetRolePermissionsAsync(@RoleKeys = '1007,1008,module:User_Management')
SELECT DISTINCT rp.PermissionCode
FROM auth.RolePermission rp
WHERE rp.RoleKey IN (SELECT value FROM STRING_SPLIT(@RoleKeys, ','));

-- GetUserPermissionsAsync(@UserId)
SELECT UserId, PermissionCode, Effect
FROM auth.UserPermission
WHERE UserId = @UserId AND (ValidToUtc IS NULL OR ValidToUtc > SYSUTCDATETIME());

-- GetUserScopesAsync(@UserId)
SELECT UserId, ScopeType, ScopeValue FROM auth.UserScope WHERE UserId = @UserId;

-- GetMenuAsync()
SELECT Id, ParentId, Title, Href, Icon, SortOrder, Permission, IsActive
FROM auth.Menu WHERE IsActive = 1;

-- SyncAsync: for each resource/permission from the app (MERGE = insert if missing, never delete)
MERGE auth.AccessResource AS t
USING (SELECT @Code AS Code, @Module AS Module, @Name AS Name, @Href AS Href) AS s ON t.Code = s.Code
WHEN MATCHED THEN UPDATE SET Module = s.Module, Name = s.Name, Href = s.Href
WHEN NOT MATCHED THEN INSERT (Code, Module, Name, Href) VALUES (s.Code, s.Module, s.Name, s.Href);

IF NOT EXISTS (SELECT 1 FROM auth.Permission WHERE Code = @PermissionCode)
    INSERT INTO auth.Permission (Code, ResourceCode, Action) VALUES (@PermissionCode, @Code, @Action);

-- SyncAsync seed: only when the tables are still empty
-- IF NOT EXISTS (SELECT 1 FROM auth.RolePermission) → insert every seed.RoleGrants row
-- IF NOT EXISTS (SELECT 1 FROM auth.Menu)           → SET IDENTITY_INSERT auth.Menu ON; insert seed.Menu with its Ids (ParentId refers to them); OFF
