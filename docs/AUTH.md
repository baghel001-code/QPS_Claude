# Sign-in, sign-out and password reset (interactive Blazor Server)

Self-contained module: `Admin/Auth/*` (services + HTTP endpoints) and
`Admin/Components/Pages/Account/*` (MudBlazor pages, all `InteractiveServer`).

## The one rule that shapes everything

**An interactive page cannot set or clear a cookie.** After the first page load it talks to the
server over a WebSocket (the Blazor circuit). There is no HTTP response to put a `Set-Cookie`
header on. So each flow is split:

| Step | Where it runs | Why there |
|---|---|---|
| Show the form, validate input, check the password, lockout, messages | Interactive page (circuit) | Rich UI, no page reloads |
| Write / clear the auth cookie | Minimal-API endpoint (real HTTP POST) | Only an HTTP response can do it |

## Login

```
Browser (interactive page)            Server (circuit)                   Server (HTTP endpoint)
───────────────────────────           ────────────────                   ──────────────────────
1 user types, clicks Sign in ───────► AccountService.ValidateCredentials
                                        · unknown user → dummy hash check (same timing)
                                        · locked? wrong pw → count++, lock after 5
                                        · disabled? rehash if needed
                                      LoginTicketStore.Issue(userId)
                                        · 32 random bytes, 60 s, single use
2 hidden <form> rendered  ◄──────────  (ticket + antiforgery token + returnUrl, NO password)
3 JS form.submit()  ── POST /account/complete-login ──────────────────► validate antiforgery
                                                                         redeem ticket (once)
                                                                         reload user, build claims
                                                                         SignInAsync → Set-Cookie
4 302 to returnUrl (local only)  ◄────────────────────────────────────  LocalRedirect
5 full page load → new circuit, now authenticated
```

Protections: generic error text; lockout (5 tries / 15 min, configurable); constant-ish timing;
hashing with the application's own `IPasswordHasher` (`PasswordHasher`); the password never leaves
the circuit and is cleared from memory; one-time ticket; antiforgery on the POST stops
login-CSRF; `ReturnUrl` restricted to local paths (no open redirect); session cleared on sign-in.

## CAPTCHA (login and forgot password)

Self-hosted, no external scripts (the site's CSP only allows scripts from itself).
**No session:** `CaptchaService` keeps the answer in server memory under a random id; on the
interactive page that id stays in the component's server-side state, so the browser only
receives the PNG (as a data: URI, allowed by `img-src 'self' data:`), and the check runs in
server code. The image is drawn by the company VmmCaptcha library through `VmmCaptchaGenerator`
(registered by `AddAppAuthentication`). If it throws, the field shows an error with a refresh
button and sign-in stays blocked until a code loads. Each image is checked once, right or wrong,
then replaced, and expires after 5 minutes. The login page checks it before the password, so
a script can't try passwords without solving a new image every time; lockout still applies.
Not on Reset password (the e-mailed token already proves the request) or Logout.
Limits: a simple image CAPTCHA slows scripted attacks but can be read by determined OCR, and
it has no audio version, so keep the refresh button and a helpdesk route for users who can't read it.

## Home page, dashboards and menu

Everyone lands on **`/`** after signing in (only users who must change their password go to
`Auth:LandingPages:ChangePassword` first; a real `ReturnUrl` from a protected page still wins).

`Components/Pages/Home.razor` uses `DashboardLayout` (top bar, left menu, footer) and shows one
dashboard picked by `HomeDashboards` from the user's **primary role**:

| Priority | Role ID | Name | Dashboard |
|---|---|---|---|
| — | Vendor | Vendor | Vendor |
| 1 | 1 | Administrator | Administrator |
| 2 | 5 | Admin | Admin |
| 3 | 1008 | QA Admin | QA admin |
| 4 | 1007 | QAM | QA manager |
| 5 | 1006 | QA | QA |
| 6 | 1010 | Category Head | Category head |
| 7 | 1011 | Associate Category Head | Associate category head |
| 8 | 1012 | Merchandiser | Merchandiser |
| 9 | 2 | Buyer | Buyer |
| 10 | 3 | QC | QC |
| 11 | 1014 | ASN | ASN |
| 12 | 1013 | BFT | BFT |
| 13 | 1009 | CM | CM |
| 14 | 4 | View | Overview (read-only) |
| — | none | | "No role yet" |

All dashboards are dummies with **sample data** (`DashboardCatalog`), rendered by `RoleDashboard`.
To replace one with a real component, register it in `HomeDashboards.Custom`.

The left menu (`Navigation/AppMenu.cs`) follows the old `NavMenu.razor` rules, using role IDs
(`Auth/QpsRoles.cs`) and the `User_Management` / `Menu` modules (claims from `AuthUser.Modules`).
Differences: Category Head (1010) now gets the inspection links (the old code tested `isACH || isACH`),
groups with the same title are merged, and duplicate links are dropped. Admin (5), BFT (1013) and
CM (1009) have no menu groups yet, as before. Dynamic items from `GET_MENUS_LIST` still need adding
under the "Menu" group.

## Logout

`LogoutForm` component = hidden `<form method=post action=/account/sign-out>` + antiforgery token.
Calling `SubmitAsync()` posts it; the endpoint calls `SignOutAsync`, clears the session, sends
`Clear-Site-Data: "cache"` and redirects to `/Account/Login?reason=signedout`.
GET never signs out, so a link or `<img>` on another site can't log users out.
`/Account/Logout` is a confirmation page with a Sign out button.

## Forgot / reset password

1. `/Account/Forgot-Password`: user enters username or e-mail. The page **always** shows the same
   "if an account matches…" message and pads the response to 1 s, so it can't be used to find accounts.
2. If the account exists: random 32-byte token → only its **SHA-256 hash** is stored, 30 min expiry,
   any older token for that user is replaced. The link is built from `Auth:PublicBaseUrl`,
   never from the request Host header (host-header poisoning would send tokens to an attacker).
3. `/Account/Reset-Password?token=…`: token checked on load; new password checked against the
   policy (min length, block-list, not containing username) **before** the token is used up;
   then the token is consumed atomically, the hash saved, the **security stamp rotated** (every
   other session of that user ends) and the lockout cleared.

## Keeping sessions honest

| Check | Where | When |
|---|---|---|
| Idle timeout | cookie `ExpireTimeSpan` + sliding | every HTTP request |
| Absolute lifetime (`auth_time` claim) | `CookieValidator` + `AppRevalidatingAuthStateProvider` | every request / every 5 min |
| User still active, security stamp unchanged | same two | at most every 5 min |

The circuit revalidator matters because an open interactive page makes no HTTP requests; without
it a disabled user could keep working until they reload.

Cookie: `__Host-App.Auth`, `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`.

## How HttpContext behaves in an interactive page

`HttpContext` exists only while an HTTP request is being processed.

1. **Prerender** (first visit): the browser GETs `/Account/Login`; the component runs on the server
   inside that request. `HttpContext` is real here, but the response is already streaming HTML,
   so you still can't reliably set cookies.
2. **Interactive**: `blazor.web.js` opens the WebSocket; the component is created again and lives
   in the circuit. Button clicks are WebSocket messages, not HTTP requests. `[CascadingParameter]
   HttpContext` is **null**, and `IHttpContextAccessor.HttpContext` is either null or the old
   WebSocket-upgrade request, whose response was sent long ago. Calling `SignInAsync` on it throws
   "Headers are read-only, response has already started" or silently does nothing.

So none of these pages use `HttpContext`. Identity comes from `AuthenticationStateProvider`
(`[CascadingParameter] Task<AuthenticationState>`), and anything that needs the HTTP response
(cookies, session reset) happens in `AccountEndpoints` after a real form POST.

## Wiring it up (Program.cs)

```csharp
using Admin.Auth;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddAppAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IAuthUserStore, InMemoryAuthUserStore>();  // DEV: replace with your DB store (scoped is fine)
builder.Services.AddScoped<IAuthEmailSender, LoggingAuthEmailSender>();   // DEV: replace with your SMTP sender
builder.Services.AddScoped<IEmployeeAuthApi, DevEmployeeAuthApi>();       // DEV: replace with your employee API client

builder.Services.AddAuthorization(o => o.FallbackPolicy = /* your policy with anonymousPaths */);

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();            // only if you use HttpContext.Session
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapAccountEndpoints();   // /account/complete-login, /account/sign-out
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
```

Remove the old cookie setup, the old `AuthenticationStateProvider` registration and the old
login/logout pages, otherwise two handlers compete.

`appsettings.json`:

```json
"Auth": {
  "PublicBaseUrl": "https://qps.example.com",
  "IdleTimeoutMinutes": 30,
  "AbsoluteLifetimeHours": 12,
  "RevalidationMinutes": 5,
  "MaxFailedAttempts": 5,
  "LockoutMinutes": 15,
  "PasswordResetTokenMinutes": 30,
  "MinPasswordLength": 10
}
```

Sign-out menu in `MainLayout.razor` (the layout must be interactive):

```razor
@using Admin.Components.Account

<MudMenu Icon="@Icons.Material.Filled.AccountCircle" Color="Color.Inherit">
    <MudMenuItem OnClick="SignOutAsync">Sign out</MudMenuItem>
</MudMenu>
<LogoutForm @ref="_logout" />

@code {
    private LogoutForm _logout = default!;
    private Task SignOutAsync() => _logout.SubmitAsync();
}
```

`IdleTimeoutMonitor.razor` already contains its own `LogoutForm` and submits it with reason `idle`.

## Employees: password checked by the employee API

Vendors and employees go through the same `AccountService.ValidateCredentialsAsync`. Only step 3
(checking the password) differs, through `ICredentialVerifier`:

| Step | Vendor | Employee |
|---|---|---|
| 1. Find user (`IAuthUserStore.FindByLoginAsync`) | QPS vendor row | QPS user row: roles, modules, active, lockout |
| 2. Locked out? | yes → refuse | yes → refuse, API not called |
| 3. Check password | `VendorPasswordVerifier`: `IPasswordHasher.VerifyPassword` | `EmployeeApiVerifier`: `IEmployeeAuthApi.AuthenticateAsync(userName, password)` |
| 4. Wrong → count, lock after 5 | same | same |
| 5. Disabled in QPS? | refuse | refuse |
| 6. Success → ticket → cookie | same | same |

- The API is only called for people who have a QPS user row, so QPS can't be used to test
  MyVishal passwords of employees who aren't QPS users.
- API down, error or 15 s timeout → "Sign-in is temporarily unavailable"; not counted as a failure.
- The API's answer is accepted only if its employee code equals the QPS user name.
- Every failed attempt takes at least 1 second, so timing doesn't reveal which step failed.
- Employee rows need no password hash (`PasswordHash = ""`). They still need a `SecurityStamp`
  (any stable value; change it to end that user's sessions) and the lockout columns.

Implement `IEmployeeAuthApi` with your existing API client. Sketch (adjust names to your API):

```csharp
public sealed class QpsEmployeeAuthApi(/* your API client */) : IEmployeeAuthApi
{
    public async Task<EmployeeApiUser?> AuthenticateAsync(string userName, string password, CancellationToken ct = default)
    {
        var response = await api.CallingAPI<EmployeeLoginRes, EmployeeLoginReq>(
            AllApiNames.EmployeeLogin, new EmployeeLoginReq(userName, password));

        if (response is null)
            throw new HttpRequestException("Employee API returned nothing");      // → unavailable
        if (response.responseCode != 0)
            return null;                                                        // wrong user name / password
        return new EmployeeApiUser(response.ApiData.EmpCode, response.ApiData.Name, response.ApiData.Email);
    }
}
```

If the API can't tell "wrong password" from "server error", return `null` only for the
wrong-password response and throw for everything else.

## One session per user (no sign-in on two devices)

Optional. Switch it on by registering an `ISingleSessionGuard` (`Admin/Auth/SingleSession.cs`)
that wraps your existing session-token service:

| When | Where | Guard method | Your service |
|---|---|---|---|
| Sign-in (real HTTP request) | `AccountEndpoints.CompleteLoginAsync` | `StartAsync` | `HasActiveSessionAsync` + `IssueNewSessionAsync` |
| Every `RevalidationMinutes` (open pages and HTTP requests) | `SessionValidation.CheckAsync` | `IsCurrentAsync` | `IsSessionValidAsync` |
| Sign-out / idle | `AccountEndpoints.LogoutAsync` | `EndAsync` | your "end session" call |

- The token goes into the cookie as the claim `qps_sid`.
- New device: if another session existed, the first page shows "Your account was signed in on another
  device…". This replaces `Session["LoginNotice"]` (an interactive page can't read Session); the
  notice is kept in `LoginNotices`, keyed by the new session id, for 2 minutes.
- Old device: at the next check its page signs out (`DashboardLayout` posts the sign-out form with
  reason `replaced`) and the login page says "You were signed out because your account was signed in
  on another device." Its cookie is refused on the next HTTP request too (`EndedSessions`).
- How quickly the old device is signed out = `Auth:RevalidationMinutes`. Use `1` if 5 minutes is too long
  (each check is one `IsSessionValidAsync` call per open tab).

```csharp
// Program.cs
builder.Services.AddScoped<ISingleSessionGuard, QpsSingleSessionGuard>();
```

Adapter sketch (names of your service and request type may differ):

```csharp
public sealed class QpsSingleSessionGuard(
    ISessionTokenService sessionTokenService,
    ILogger<QpsSingleSessionGuard> logger) : ISingleSessionGuard
{
    // Sign-in: issue a new token (your service ends the old one).
    public async Task<StartedSession> StartAsync(AuthUser user, SessionClient client, CancellationToken ct)
    {
        var request = Request(user.Id, user.UserName, user.AccountType, null, await DescribeAsync(client));
        var hadExistingSession = await sessionTokenService.HasActiveSessionAsync(request);   // or a flag on the response
        var response = await sessionTokenService.IssueNewSessionAsync(request, ct);
        if (string.IsNullOrEmpty(response.SessionToken))
            throw new InvalidOperationException("IssueNewSessionAsync returned no token");      // → "temporarily unavailable"
        return new StartedSession(response.SessionToken, hadExistingSession);
    }

    // Every check: is this token still the user's active session?
    public async Task<bool> IsCurrentAsync(SessionOwner s, CancellationToken ct)
    {
        try
        {
            var response = await sessionTokenService.IsSessionValidAsync(Request(s.UserId, s.UserName, s.AccountType, s.Token, null), ct);
            return response.IsValid;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Session check failed for {UserId}; keeping the session until the next check", s.UserId);
            return true;   // a DB/API hiccup must not sign everyone out
        }
    }

    // Sign-out / idle: clear THIS token only.
    public Task EndAsync(SessionOwner s, CancellationToken ct) =>
        sessionTokenService.ClearSessionAsync(Request(s.UserId, s.UserName, s.AccountType, s.Token, null), ct);

    private static IssueSessionRequest Request(string authUserId, string userCode, AccountType type, string? token, string? clientInfo)
    {
        UserLoginRecord.TryParseId(authUserId, out _, out var id);           // "V:88" → 88
        var userType = type == AccountType.Vendor ? "VENDOR" : "EMPLOYEE";  // the values your table uses
        return new IssueSessionRequest(userType, userCode, (int)id, token, clientInfo);
    }

    private static async Task<string> DescribeAsync(SessionClient client)
    {
        var host = "";
        if (System.Net.IPAddress.TryParse(client.IpAddress, out var ip))
        {
            try { host = (await System.Net.Dns.GetHostEntryAsync(ip)).HostName; }   // reverse DNS can be slow
            catch (System.Net.Sockets.SocketException) { }
        }
        return $"{client.IpAddress} / {host}";
    }
}
```

## Database (implement `IAuthUserStore`)

**Employee and vendor accounts.** The login page has an Employee tab and a Vendor tab, and
`FindByLoginAsync(login, type)` receives the tab's `AccountType`, so the two can live in different
tables. `AuthUser.Id` must be unique across both (for example `E:1042` and `V:88`), because
sessions later look users up by id only. Only vendors can use Forgot password: the service
refuses employee accounts, and employees see an IT-helpdesk note instead of the link.
The user's type is in the `account_type` claim.

```sql
ALTER TABLE dbo.Users ADD
    PasswordHash     NVARCHAR(512) NOT NULL DEFAULT '',
    SecurityStamp    NVARCHAR(64)  NOT NULL DEFAULT CONVERT(NVARCHAR(64), NEWID()),
    FailedLoginCount INT           NOT NULL DEFAULT 0,
    LockoutEndUtc    DATETIMEOFFSET NULL;

CREATE TABLE dbo.PasswordResetTokens (
    TokenHash  CHAR(64)       NOT NULL PRIMARY KEY,   -- SHA-256 hex, never the raw token
    UserId     NVARCHAR(64)   NOT NULL,
    ExpiresUtc DATETIMEOFFSET NOT NULL,
    INDEX IX_PasswordResetTokens_UserId (UserId));

-- RecordFailedLoginAsync (atomic)
UPDATE dbo.Users SET FailedLoginCount = FailedLoginCount + 1
OUTPUT INSERTED.FailedLoginCount WHERE Id = @UserId;

-- ConsumePasswordResetTokenAsync (atomic, single use)
DELETE FROM dbo.PasswordResetTokens OUTPUT DELETED.UserId
WHERE TokenHash = @TokenHash AND ExpiresUtc > SYSUTCDATETIME();
```

Existing passwords keep working: the module verifies them with the same `PasswordHasher`
that created them (`VerifyPassword`) and stores new ones with `HashPassword`. The namespace of
`IPasswordHasher` is set once in `Admin/Auth/AuthUsings.cs`.

## Notes and limits

- The render mode is set on each page. If `App.razor` already has `<Routes @rendermode="InteractiveServer" />`,
  the page directive is redundant but harmless.
- Antiforgery tokens for the two POST forms come from `GET /account/antiforgery-token`, fetched by
  `auth.js` just before submitting, so the forms work with or without prerendering.
- `LoginTicketStore` is in memory. With several servers use sticky sessions (Blazor Server needs
  them anyway) or move it to Redis.
- The lockout message tells a guesser that the account exists; that's the usual trade-off
  (ASP.NET Identity does the same). The login circuit isn't covered by HTTP rate limiting, so add
  per-IP limits at the reverse proxy/WAF if the site is internet-facing.
- Dev user in `InMemoryAuthUserStore`: `admin` / `ChangeMe!2026`.
