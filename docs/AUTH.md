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
PBKDF2-SHA512 hashing with automatic upgrade (`PasswordHasher<T>`); the password never leaves
the circuit and is cleared from memory; one-time ticket; antiforgery on the POST stops
login-CSRF; `ReturnUrl` restricted to local paths (no open redirect); session cleared on sign-in.

## Logout

`LogoutForm` component = hidden `<form method=post action=/account/logout>` + antiforgery token.
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
app.MapAccountEndpoints();   // /account/complete-login, /account/logout
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

In a layout, for the user menu and the idle-timeout monitor:

```razor
<LogoutForm @ref="_logout" />
<MudMenuItem OnClick="() => _logout.SubmitAsync()">Sign out</MudMenuItem>
@* idle timeout: await _logout.SubmitAsync("idle"); *@
```

## Database (implement `IAuthUserStore`)

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

Existing passwords: `PasswordHasher<T>` can't verify hashes made by another algorithm. Either
force a reset for everyone, or in your store verify the old format once and re-save with
`PasswordHasher<T>` on the next successful login.

## Notes and limits

- The render mode is set on each page. If `App.razor` already has `<Routes @rendermode="InteractiveServer" />`,
  the page directive is redundant but harmless.
- Keep prerendering on (the default). The `<AntiforgeryToken />` inside interactive components
  gets its token from the prerendered response.
- `LoginTicketStore` is in memory. With several servers use sticky sessions (Blazor Server needs
  them anyway) or move it to Redis.
- The lockout message tells a guesser that the account exists; that's the usual trade-off
  (ASP.NET Identity does the same). The login circuit isn't covered by HTTP rate limiting, so add
  per-IP limits at the reverse proxy/WAF if the site is internet-facing.
- Dev user in `InMemoryAuthUserStore`: `admin` / `ChangeMe!2026`.
