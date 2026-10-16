using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Admin;
using ApolloData.Api.Documents;
using ApolloData.Api.Domain;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApolloData.Api.Email;

/// <summary>Outbox worker settings, bound from the "EmailOutbox" configuration section.</summary>
public sealed class EmailOutboxOptions
{
    /// <summary>Fallback polling interval; new rows also wake the worker immediately via <see cref="EmailOutboxSignal"/>.</summary>
    public int PollIntervalMilliseconds { get; set; } = 15_000;

    public int MaxAttempts { get; set; } = 6;
    public int BatchSize { get; set; } = 20;

    /// <summary>Delay before the first retry; doubles with every further failure (capped at 30 minutes).</summary>
    public int RetryBaseSeconds { get; set; } = 30;
}

/// <summary>Wakes the outbox worker right after a transaction that enqueued email has committed.</summary>
public sealed class EmailOutboxSignal : WorkSignal;

public static class EmailOutbox
{
    /// <summary>
    /// Adds a notification email to the current unit of work. It is committed together with the change that
    /// caused it and delivered by <see cref="EmailOutboxWorker"/>. Demo clinics never receive email.
    /// </summary>
    public static void EnqueueEmail(this AppDbContext db, OutboundEmailKind kind, Clinic clinic, EmailMessage message, DateTime now)
    {
        if (clinic.IsDemo) return;
        db.OutboundEmails.Add(new OutboundEmail
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            ClinicId = clinic.Id,
            ToEmail = message.ToEmail,
            ToName = message.ToName,
            Subject = message.Subject,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }
}

/// <summary>
/// Delivers queued notification emails. Each row is sent and marked in its own small transaction; failures are
/// retried with exponential back-off and given up (with an audit event) after <see cref="EmailOutboxOptions.MaxAttempts"/>.
/// Designed for a single API instance; several instances would need row claiming (e.g. FOR UPDATE SKIP LOCKED).
/// </summary>
internal sealed class EmailOutboxWorker(
    IServiceScopeFactory scopes,
    EmailOutboxSignal signal,
    IOptions<EmailOutboxOptions> options,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(100, options.Value.PollIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // E.g. the database is briefly unavailable; the next round retries.
                logger.LogWarning(ex, "Email outbox processing failed");
            }

            try
            {
                await signal.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Sends every due email once. Returns the number of rows that were processed.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var sender = services.GetRequiredService<IEmailSender>();
        var time = services.GetRequiredService<TimeProvider>();
        var settings = options.Value;

        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.OutboundEmails
            .Where(e => e.SentAt == null && e.FailedAt == null && e.NextAttemptAt <= now)
            .OrderBy(e => e.CreatedAt)
            .Take(settings.BatchSize)
            .ToListAsync(ct);

        foreach (var email in due)
        {
            email.Attempts++;
            try
            {
                var message = new EmailMessage(email.ToEmail, email.ToName, email.Subject, email.TextBody, email.HtmlBody,
                    await AttachmentsAsync(services, db, email, ct));
                await sender.SendAsync(message, ct);
                email.SentAt = time.GetUtcNow().UtcDateTime;
                email.LastError = null;
                db.Record(ActivityEventType.EmailSent, email.SentAt.Value, email.ClinicId, detail: email.Kind.ToString());
            }
            catch (EmailDeliveryException ex)
            {
                var failedAt = time.GetUtcNow().UtcDateTime;
                email.LastError = Describe(ex);
                if (email.Attempts >= settings.MaxAttempts)
                {
                    email.FailedAt = failedAt;
                    db.Record(ActivityEventType.EmailFailed, failedAt, email.ClinicId, detail: $"{email.Kind};attempts={email.Attempts}");
                    logger.LogError("Giving up email {EmailId} ({Kind}) after {Attempts} attempts", email.Id, email.Kind, email.Attempts);
                }
                else
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(settings.RetryBaseSeconds * Math.Pow(2, email.Attempts - 1), 1800));
                    email.NextAttemptAt = failedAt.Add(delay);
                    logger.LogWarning("Email {EmailId} ({Kind}) failed (attempt {Attempts}); retrying in {Delay}",
                        email.Id, email.Kind, email.Attempts, delay);
                }
            }
            await db.SaveChangesAsync(ct);
        }
        return due.Count;
    }

    /// <summary>
    /// The registration confirmation carries the PDF overview. It is generated when the e-mail is sent
    /// (nothing binary is stored in the outbox); if the clinic was deleted meanwhile, the e-mail goes without it.
    /// </summary>
    private static async Task<IReadOnlyList<EmailAttachment>?> AttachmentsAsync(
        IServiceProvider services, AppDbContext db, OutboundEmail email, CancellationToken ct)
    {
        if (email.Kind != OutboundEmailKind.RegistrationConfirmation || email.ClinicId is not { } clinicId) return null;
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return null;

        var app = services.GetRequiredService<AppInfo>();
        var clock = services.GetRequiredService<ReportingClock>();
        var pdf = ConfirmationPdf.Generate(clinic, app.Name, clock.UtcNow, clock.TimeZone);
        return [new EmailAttachment(ConfirmationPdf.FileName(clinic), "application/pdf", pdf)];
    }

    private static string Describe(EmailDeliveryException ex)
    {
        var inner = ex.InnerException;
        var status = inner is SmtpCommandException command ? $", SMTP {(int)command.StatusCode}" : "";
        var text = inner is null ? ex.Message : $"{inner.GetType().Name}{status}";
        return text.Length > 500 ? text[..500] : text;
    }
}
