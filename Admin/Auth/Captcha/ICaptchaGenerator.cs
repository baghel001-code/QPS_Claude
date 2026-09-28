namespace Admin.Auth.Captcha;

/// <summary>A new CAPTCHA: the answer (kept on the server only) and the image to show.</summary>
/// <param name="Answer">The text the user must type.</param>
/// <param name="ImageBase64">PNG bytes as base64, without a "data:" prefix.</param>
public sealed record GeneratedCaptcha(string Answer, string ImageBase64);

/// <summary>
/// Draws CAPTCHA images. <see cref="CaptchaService"/> stores the answer and checks it;
/// the generator only makes the text and the picture. Register one as a singleton.
/// </summary>
public interface ICaptchaGenerator
{
    GeneratedCaptcha Generate();
}

/// <summary>Built-in generator (no external libraries). Used unless another one is registered.</summary>
public sealed class BuiltInCaptchaGenerator : ICaptchaGenerator
{
    public GeneratedCaptcha Generate()
    {
        var chars = new char[5];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CaptchaImage.Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(CaptchaImage.Alphabet.Length)];
        var answer = new string(chars);
        return new GeneratedCaptcha(answer, Convert.ToBase64String(CaptchaImage.RenderPng(answer)));
    }
}
