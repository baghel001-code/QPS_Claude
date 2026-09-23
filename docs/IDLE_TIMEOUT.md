# Idle session timeout

A user with no mouse, keyboard, touch or scroll activity for `MaxSessionTime` minutes sees a
60-second warning, then gets logged out and their auth cookie is cleared.

## How it works

| Piece | Role |
|---|---|
| `wwwroot/js/idle-timeout.js` | Listens for activity in the browser. Reports it to the circuit at most once per 15 s, shares it across tabs through `localStorage`, and calls the keep-alive endpoint so the cookie keeps sliding while the user is active. |
| `Components/Layout/IdleTimeoutMonitor.razor` | Ticks every 1 s. Shows the warning overlay in the last 60 s, then navigates to `/Account/User-Logout?reason=idle` with `forceLoad: true`, which clears the cookie. |
| `Services/UserActivityState.cs` | Per-circuit (scoped) timestamp of the last activity. |
| `Services/CustomRevalidatingAuthenticationStateProvider.cs` | Server-side backstop. Runs the idle check before the DB checks, and tolerates up to 5 back-to-back transient failures instead of logging the user out on the first exception. |
| `Controllers/SessionKeepAliveController.cs` | `POST api/session/keepalive` renews the sliding auth cookie and `HttpContext.Session`. Blazor clicks go over the WebSocket and never renew these on their own. |
| `Program.cs` | SignalR keep-alive set back to 15 s / 30 s (the old `MaxSessionTime/2` caused reconnect loops on idle pages). Session `IdleTimeout` now equals `MaxSessionTime`. Registers `IUserActivityState`. |

## Changes to make in files that are not in this commit

1. **Enum**: add `IdleTimeout` to `SessionInvalidReason`:
   ```csharp
   public enum SessionInvalidReason { /* existing values */, Expired, ForcedLogout, IdleTimeout }
   ```
2. **MainLayout.razor**: add the monitor once, anywhere inside the layout:
   ```razor
   <IdleTimeoutMonitor />
   ```
   If `MainLayout` lives in a namespace other than `Admin.Components.Layout`, add
   `@using Admin.Components.Layout` to `_Imports.razor`. The layout must be interactive
   (`<Routes @rendermode="InteractiveServer" />` in `App.razor`).
3. **appsettings.json**: lower the revalidation frequency. The idle check no longer depends on it:
   ```json
   "MaxSessionTime": 30,
   "UserRevalidateInSeconds": 60
   ```
4. **Absolute session lifetime (`session_expires` claim)**. This forces a re-login after N hours
   even for active users. Cookie auth has no `exp` claim, so the app issues its own:
   - `CustomClaimTypes`:
     ```csharp
     public const string SessionExpires = "session_expires";
     ```
   - `AppConfigurationSettings`:
     ```csharp
     public int AbsoluteSessionHours { get; set; } = 12;
     ```
   - `appsettings.json` → `"AbsoluteSessionHours": 12`
   - Login page, where the claims are built before `SignInAsync`:
     ```csharp
     claims.Add(new Claim(CustomClaimTypes.SessionExpires,
         DateTimeOffset.UtcNow.AddHours(_settings.AbsoluteSessionHours).ToUnixTimeSeconds().ToString()));
     ```
   Users who signed in before this change have no claim and are not affected until their next login.
5. **Optional**: have the logout page carry `reason=idle` over to the login page and show
   *"You were logged out after 30 minutes of inactivity."*

## Tuning

- Timeout: `AppConfigurationSettings:MaxSessionTime` (minutes).
- Warning length and report throttle: constants at the top of `IdleTimeoutMonitor.razor`.
