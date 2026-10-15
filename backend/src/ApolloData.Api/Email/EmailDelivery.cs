using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ApolloData.Api.Email;

/// <summary>
/// SMTP settings read from the flat environment variables required by the assignment
/// (<c>SMTP_HOST</c>, <c>SMTP_PORT</c>, ...). Credentials are never hardcoded or logged.
/// </summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromEmail { get; set; }
    public string FromName { get; set; } = "ApolloData";

    /// <summary>true = STARTTLS (required by Gmail on port 587). false = implicit TLS on port 465, otherwise plain SMTP.</summary>
    public bool UseStartTls { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromEmail ?? Username);

    public static void Bind(SmtpOptions options, IConfiguration configuration)
    {
        options.Host = configuration["SMTP_HOST"];
        if (int.TryParse(configuration["SMTP_PORT"], out var port)) options.Port = port;
        options.Username = configuration["SMTP_USERNAME"];
        options.Password = configuration["SMTP_PASSWORD"];
        options.FromEmail = configuration["SMTP_FROM_EMAIL"] is { Length: > 0 } from ? from : configuration["SMTP_USERNAME"];
        if (configuration["SMTP_FROM_NAME"] is { Length: > 0 } fromName) options.FromName = fromName;
        if (bool.TryParse(configuration["SMTP_USE_STARTTLS"], out var startTls)) options.UseStartTls = startTls;
    }
}

public sealed record EmailMessage(string ToEmail, string? ToName, string Subject, string TextBody, string HtmlBody);

/// <summary>Thrown when the message could not be handed over to the SMTP server.</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public interface IEmailSender
{
    /// <exception cref="EmailDeliveryException">The message was not accepted by the SMTP server.</exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

internal sealed class MailKitEmailSender(IOptions<SmtpOptions> options, ILogger<MailKitEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (!smtp.IsConfigured)
        {
            logger.LogError("SMTP is not configured (SMTP_HOST / SMTP_FROM_EMAIL missing); email was not sent");
            throw new EmailDeliveryException("SMTP is not configured.");
        }

        var host = smtp.Host!;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.FromEmail ?? smtp.Username!));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        var socketOptions = smtp.UseStartTls
            ? SecureSocketOptions.StartTls
            : smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;

        using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };
        try
        {
            await client.ConnectAsync(host, smtp.Port, socketOptions, cancellationToken);
            if (!string.IsNullOrEmpty(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
            }
            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            // Recipient is logged at domain level only.
            logger.LogInformation("Email \"{Subject}\" delivered to SMTP server {Host}:{Port} for recipient domain {RecipientDomain}",
                message.Subject, smtp.Host, smtp.Port, RecipientDomain(message.ToEmail));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Safe diagnostics only: host, port, exception type, SMTP status code. Never the password or message body.
            var smtpStatus = ex is SmtpCommandException command ? command.StatusCode.ToString() : null;
            logger.LogWarning("SMTP delivery failed via {Host}:{Port} ({ExceptionType}, SMTP status {SmtpStatus}): {Reason}",
                smtp.Host, smtp.Port, ex.GetType().Name, smtpStatus ?? "n/a", ex.Message);
            throw new EmailDeliveryException("The email could not be delivered.", ex);
        }
    }

    private static string RecipientDomain(string email) => email[(email.IndexOf('@') + 1)..];
}
