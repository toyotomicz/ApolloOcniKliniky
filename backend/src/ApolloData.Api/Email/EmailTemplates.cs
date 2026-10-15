using System.Net;
using ApolloData.Api.Domain;

namespace ApolloData.Api.Email;

/// <summary>
/// Plain-text + HTML emails in the Apollo look (charcoal header, red accent). HTML uses inline styles and
/// tables because many email clients ignore stylesheets. All user-provided values are HTML-encoded.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage LoginCode(string appName, string toEmail, string recipientName, string code, int validMinutes)
    {
        var subject = $"{appName}: jednorázový přihlašovací kód";
        var text =
            $"""
            Dobrý den,

            váš jednorázový kód pro přihlášení do portálu {appName} je:

                {code}

            Kód je platný {validMinutes} minut a lze jej použít pouze jednou.
            Pokud jste o kód nežádali, tento e-mail ignorujte.

            {appName}
            """;

        var body =
            $"""
            <p style="margin:0 0 16px;">{Greeting(recipientName)}</p>
            <p style="margin:0 0 16px;">váš jednorázový kód pro přihlášení do portálu je:</p>
            <p style="margin:0 0 20px;font-size:32px;font-weight:700;letter-spacing:.3em;font-family:Consolas,monospace;">{code}</p>
            <p style="margin:0 0 8px;color:#5c6670;font-size:14px;">Kód je platný {validMinutes} minut a lze jej použít pouze jednou.</p>
            <p style="margin:0;color:#5c6670;font-size:14px;">Pokud jste o kód nežádali, tento e-mail ignorujte.</p>
            """;

        return new EmailMessage(toEmail, recipientName, subject, text, Layout(appName, "Přihlašovací kód", body));
    }

    /// <summary>Sent when an administrator approves, rejects or returns a registration for review.</summary>
    public static EmailMessage StatusChanged(string appName, Clinic clinic, string? message, string portalUrl)
    {
        var contact = $"{clinic.ContactFirstName} {clinic.ContactLastName}";
        var (subject, headline, explanation, messageLabel) = clinic.Status switch
        {
            ClinicStatus.Approved => (
                $"{appName}: registrace kliniky byla schválena",
                "Registrace byla schválena",
                $"registrace kliniky {clinic.Name} byla schválena. Údaje kliniky můžete dál spravovat v portálu.",
                "Zpráva od administrátora"),
            ClinicStatus.Rejected => (
                $"{appName}: registrace kliniky byla zamítnuta",
                "Registrace byla zamítnuta",
                $"registrace kliniky {clinic.Name} byla zamítnuta. Důvod je uveden níže. Po úpravě údajů může administrátor registraci posoudit znovu.",
                "Důvod zamítnutí"),
            _ => (
                $"{appName}: registrace kliniky se znovu posuzuje",
                "Registrace se znovu posuzuje",
                $"registrace kliniky {clinic.Name} byla vrácena k posouzení. O výsledku vás budeme informovat.",
                "Zpráva od administrátora"),
        };

        var text =
            $"""
            Dobrý den, {contact},

            {explanation}
            {(message is null ? "" : $"\n{messageLabel}:\n{message}\n")}
            Aktuální stav najdete v portálu kliniky (přihlášení kódem z e-mailu):
            {portalUrl}

            {appName}
            """;

        var accent = clinic.Status switch
        {
            ClinicStatus.Approved => "#1d7348",
            ClinicStatus.Rejected => "#b42318",
            _ => "#8a5a00",
        };
        var messageHtml = message is null
            ? ""
            : $"""
              <div style="margin:0 0 20px;padding:14px 16px;background:#f5f6f7;border-left:3px solid {accent};border-radius:4px;">
                <div style="font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:#5c6670;font-weight:600;margin-bottom:6px;">{Encode(messageLabel)}</div>
                <div style="white-space:pre-wrap;">{Encode(message)}</div>
              </div>
              """;
        var body =
            $"""
            <p style="margin:0 0 16px;">{Greeting(contact)}</p>
            <p style="margin:0 0 20px;">{Encode(explanation)}</p>
            {messageHtml}
            {Button(portalUrl, "Otevřít portál kliniky")}
            """;

        return new EmailMessage(clinic.Email, contact, subject, text, Layout(appName, headline, body, accent));
    }

    /// <summary>
    /// Notification about a new administrator message. The message itself is intentionally not included:
    /// it is read in the authenticated portal, so the email never carries the conversation.
    /// </summary>
    public static EmailMessage NewMessage(string appName, Clinic clinic, string portalUrl)
    {
        var contact = $"{clinic.ContactFirstName} {clinic.ContactLastName}";
        var subject = $"{appName}: nová zpráva od administrátora";
        var text =
            $"""
            Dobrý den, {contact},

            v portálu kliniky {clinic.Name} na vás čeká nová zpráva od administrátora.
            Přečtete si ji po přihlášení kódem z e-mailu:
            {portalUrl}

            {appName}
            """;
        var body =
            $"""
            <p style="margin:0 0 16px;">{Greeting(contact)}</p>
            <p style="margin:0 0 20px;">v portálu kliniky <strong>{Encode(clinic.Name)}</strong> na vás čeká nová zpráva od administrátora.</p>
            {Button(portalUrl, "Přečíst zprávu")}
            """;
        return new EmailMessage(clinic.Email, contact, subject, text, Layout(appName, "Nová zpráva", body));
    }

    private static string Greeting(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Dobrý den," : $"Dobrý den, {Encode(name)},";

    private static string Button(string url, string label) =>
        $"""<p style="margin:0 0 8px;"><a href="{Encode(url)}" style="display:inline-block;padding:12px 24px;background:#c93a32;color:#ffffff;text-decoration:none;border-radius:24px;font-weight:600;">{Encode(label)}</a></p>""";

    private static string Layout(string appName, string title, string bodyHtml, string accent = "#c93a32") =>
        $"""
        <!doctype html>
        <html lang="cs">
        <body style="margin:0;padding:24px;background:#f4f5f6;font-family:'Open Sans',Segoe UI,Arial,sans-serif;color:#212529;line-height:1.5;">
          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e3e5e8;border-radius:8px;overflow:hidden;">
            <tr><td style="background:#313336;padding:18px 32px;color:#ffffff;font-weight:700;font-size:18px;letter-spacing:.02em;">{Encode(appName)}</td></tr>
            <tr><td style="height:3px;background:{accent};font-size:0;line-height:0;">&nbsp;</td></tr>
            <tr><td style="padding:28px 32px;">
              <h1 style="margin:0 0 16px;font-size:20px;color:#14171a;">{Encode(title)}</h1>
              {bodyHtml}
            </td></tr>
            <tr><td style="padding:16px 32px;background:#f5f6f7;color:#5c6670;font-size:12px;">Tento e-mail byl odeslán automaticky z portálu {Encode(appName)}. Neodpovídejte na něj prosím.</td></tr>
          </table>
        </body>
        </html>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
