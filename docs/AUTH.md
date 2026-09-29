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

## Landing page after sign-in

`LandingPageResolver` picks the page from the claims set at sign-in, in this order:

1. `must_change_password` claim (`AuthUser.MustChangePassword`) → `ChangePassword`, always first
2. Vendor → `Vendor`
3. Employee → first entry in `EmployeeRoles` whose role the user has (case-insensitive), else `EmployeeDefault`

If the user was sent to the login page from a protected page (a real `ReturnUrl`), they go back
there instead of the dashboard. Paths are configured, not hard-coded:

```json
"Auth": {
  "LandingPages": {
    "ChangePassword": "/changepassword",
    "Vendor": "/VendorDashboard",
    "EmployeeDefault": "/EmployeeDashboard",
    "EmployeeRoles": [
      { "Role": "Admin", "Path": "/AdminDashboard" },
      { "Role": "QAM",   "Path": "/QAMDashboard" },
      { "Role": "QA",    "Path": "/QADashboard" },
      { "Role": "Buyer", "Path": "/BuyerDashboard" }
    ]
  }
}
```

Role names must match what your `IAuthUserStore` puts in `AuthUser.Roles`. Put `<RedirectToLanding />`
on the home page so opening "/" also takes signed-in users to their dashboard.

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
