using System.Security.Claims;
using ApolloData.Api.Auth;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;
using ApolloData.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Messages;

/// <param name="AuthorName">Administrator username for administrators; clinics only see "Administrátor".</param>
/// <param name="ReadAt">When the recipient first read the message (as stored before this request marked anything read).</param>
public sealed record MessageDto(Guid Id, MessageSender Sender, string? AuthorName, string Body, DateTime CreatedAt, DateTime? ReadAt);

public sealed record SendMessageRequest(string? Body);

/// <summary>
/// Conversation between one clinic and the administrators. Clinics address their own thread only through the
/// session (no clinic id in the route); administrators address any clinic by id.
/// Reading a thread marks the other side's messages as read.
/// </summary>
public static class MessageEndpoints
{
    public const int MaxBodyLength = 4000;

    public static void MapClinicMessageEndpoints(this RouteGroupBuilder clinicGroup)
    {
        clinicGroup.MapGet("/messages", ClinicListAsync);
        clinicGroup.MapPost("/messages", ClinicSendAsync).RequireRateLimiting(RateLimitPolicies.Messages);
    }

    public static void MapAdminMessageEndpoints(this RouteGroupBuilder adminGroup)
    {
        adminGroup.MapGet("/clinics/{id:guid}/messages", AdminListAsync);
        adminGroup.MapPost("/clinics/{id:guid}/messages", AdminSendAsync);
    }

    private static async Task<IResult> ClinicListAsync(ClaimsPrincipal user, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var clinicId = user.GetSubjectId();
        var messages = await LoadThreadAsync(db, clinicId, forAdministrator: false, ct);
        await MarkReadAsync(db, clinicId, MessageSender.Administrator, time, ct);
        return TypedResults.Ok(messages);
    }

    private static async Task<IResult> ClinicSendAsync(
        SendMessageRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, IAdminNotifier notifier, CancellationToken ct)
    {
        var body = ValidateBody(request, out var errors);
        if (body is null) return ApiProblems.Validation(errors);

        var clinicId = user.GetSubjectId();
        var clinic = await db.Clinics.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicId, ct);
        if (clinic is null) return ApiProblems.NotFound();

        var now = time.GetUtcNow().UtcDateTime;
        var message = new ClinicMessage { Id = Guid.CreateVersion7(), ClinicId = clinicId, Sender = MessageSender.Clinic, Body = body, CreatedAt = now };
        db.ClinicMessages.Add(message);
        db.Record(ActivityEventType.ClinicMessageSent, now, clinicId);
        await db.SaveChangesAsync(ct);

        await notifier.MessageReceivedAsync(new MessageReceivedEvent(clinicId, clinic.Name, now));
        return TypedResults.Created("/api/clinic/messages", ToDto(message, null, forAdministrator: false));
    }

    private static async Task<IResult> AdminListAsync(Guid id, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Clinics.AnyAsync(c => c.Id == id, ct)) return ApiProblems.NotFound("Klinika nebyla nalezena.");
        var messages = await LoadThreadAsync(db, id, forAdministrator: true, ct);
        await MarkReadAsync(db, id, MessageSender.Clinic, time, ct);
        return TypedResults.Ok(messages);
    }

    private static async Task<IResult> AdminSendAsync(
        Guid id, SendMessageRequest request, ClaimsPrincipal user, AppDbContext db, TimeProvider time, AppInfo app,
        EmailOutboxSignal outbox, CancellationToken ct)
    {
        var body = ValidateBody(request, out var errors);
        if (body is null) return ApiProblems.Validation(errors);

        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (clinic is null) return ApiProblems.NotFound("Klinika nebyla nalezena.");

        var adminId = user.GetSubjectId();
        var now = time.GetUtcNow().UtcDateTime;
        var message = new ClinicMessage
        {
            Id = Guid.CreateVersion7(), ClinicId = id, Sender = MessageSender.Administrator, AdministratorId = adminId, Body = body, CreatedAt = now,
        };
        db.ClinicMessages.Add(message);
        db.Record(ActivityEventType.AdminMessageSent, now, id, adminId);
        // The email only says that a message is waiting; the text stays in the authenticated portal.
        db.EnqueueEmail(OutboundEmailKind.NewMessage, clinic, EmailTemplates.NewMessage(app.Name, clinic, app.ClinicLoginUrl), now);
        await db.SaveChangesAsync(ct);
        outbox.Notify();

        var username = await db.Administrators.Where(a => a.Id == adminId).Select(a => a.Username).FirstAsync(ct);
        return TypedResults.Created($"/api/admin/clinics/{id}/messages", ToDto(message, username, forAdministrator: true));
    }

    private static string? ValidateBody(SendMessageRequest request, out ValidationErrors errors)
    {
        errors = new ValidationErrors();
        return Rules.Required(errors, "body", request.Body, MaxBodyLength);
    }

    private static async Task<List<MessageDto>> LoadThreadAsync(AppDbContext db, Guid clinicId, bool forAdministrator, CancellationToken ct)
    {
        var rows = await db.ClinicMessages.AsNoTracking()
            .Where(m => m.ClinicId == clinicId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { Message = m, Username = m.Administrator != null ? m.Administrator.Username : null })
            .ToListAsync(ct);
        return rows.Select(r => ToDto(r.Message, r.Username, forAdministrator)).ToList();
    }

    private static Task<int> MarkReadAsync(AppDbContext db, Guid clinicId, MessageSender from, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return db.ClinicMessages
            .Where(m => m.ClinicId == clinicId && m.Sender == from && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), ct);
    }

    private static MessageDto ToDto(ClinicMessage m, string? username, bool forAdministrator) => new(
        m.Id,
        m.Sender,
        m.Sender == MessageSender.Administrator ? (forAdministrator ? username : "Administrátor") : null,
        m.Body,
        m.CreatedAt,
        m.ReadAt);
}
