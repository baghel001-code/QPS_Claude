namespace Admin.Auth.Captcha;

/// <summary>
/// Draws CAPTCHA images with the company's VmmCaptcha library, using the same settings as the
/// old login page. Registered by AddAppAuthentication. Only the drawing comes from VmmCaptcha:
/// the answer is kept by CaptchaService in server memory, never in HttpContext.Session, a
/// cookie or the page.
/// </summary>
public sealed class VmmCaptchaGenerator : ICaptchaGenerator
{
    public GeneratedCaptcha Generate()
    {
        // A new instance per call: we don't know whether VmmCaptcha.Captcha is thread-safe,
        // and this singleton is called from many users' circuits at once.
        var result = new VmmCaptcha.Captcha().Generate(3, 0, null, 30, System.Drawing.FontStyle.Regular, 0);

        // The old page added the "data:image/png;base64," prefix itself, so this is plain base64.
        return new GeneratedCaptcha(result.CaptchaText, result.CaptchaBase64Image);
    }
}
