using System.Net;

namespace ApolloData.Api.Email;

public static class EmailTemplates
{
    public static EmailMessage LoginCode(string appName, string toEmail, string recipientName, string code, int validMinutes)
    {
        var subject = $"{appName}: jednorázový přihlašovací kód";
        var text =
            $"""
            Dobrý den,

            váš jednorázový kód pro přihlášení do portálu očních klinik {appName} je:

                {code}

            Kód je platný {validMinutes} minut a lze jej použít pouze jednou.
            Pokud jste o kód nežádali, tento e-mail ignorujte.

            {appName}
            """;

        var safeName = WebUtility.HtmlEncode(recipientName);
        var safeApp = WebUtility.HtmlEncode(appName);
        var html =
            $"""
            <!doctype html>
            <html lang="cs">
            <body style="margin:0;padding:24px;background:#f4f5f6;font-family:Segoe UI,Arial,sans-serif;color:#212529;">
              <table role="presentation" width="100%" style="max-width:520px;margin:0 auto;background:#ffffff;border:1px solid #e3e5e8;border-radius:8px;">
                <tr><td style="padding:28px 32px;">
                  <p style="margin:0 0 4px;font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#c93a32;font-weight:600;">{safeApp}</p>
                  <h1 style="margin:0 0 16px;font-size:20px;">Přihlašovací kód</h1>
                  <p style="margin:0 0 16px;">Dobrý den{(safeName.Length > 0 ? ", " + safeName : "")},</p>
                  <p style="margin:0 0 16px;">váš jednorázový kód pro přihlášení do portálu očních klinik je:</p>
                  <p style="margin:0 0 20px;font-size:32px;font-weight:700;letter-spacing:.3em;font-family:Consolas,monospace;">{code}</p>
                  <p style="margin:0 0 8px;color:#5c6670;font-size:14px;">Kód je platný {validMinutes} minut a lze jej použít pouze jednou.</p>
                  <p style="margin:0;color:#5c6670;font-size:14px;">Pokud jste o kód nežádali, tento e-mail ignorujte.</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new EmailMessage(toEmail, recipientName, subject, text, html);
    }
}
