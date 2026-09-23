using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Admin.Controllers
{
    /// <summary>
    /// Called by wwwroot/js/idle-timeout.js while the user is active.
    /// Blazor interaction runs over the WebSocket and never reaches the cookie / session
    /// middleware, so without this an active user's auth cookie and HttpContext.Session
    /// would expire after MaxSessionTime even though they were working the whole time.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/session")]
    public sealed class SessionKeepAliveController : ControllerBase
    {
        [HttpPost("keepalive")]
        public async Task<IActionResult> KeepAlive()
        {
            // Loading the session makes the session middleware refresh its idle timer on commit.
            // The cookie handler renews the auth cookie itself (SlidingExpiration = true).
            await HttpContext.Session.LoadAsync(HttpContext.RequestAborted);
            Response.Headers.CacheControl = "no-store";
            return NoContent();
        }
    }
}
