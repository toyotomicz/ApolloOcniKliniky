using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Email;

namespace ApolloData.Api.Admin;

/// <summary>
/// Applies an administrative status change to a tracked clinic: updates the clinic and adds the history record,
/// the audit event and the notification email to the current unit of work. The caller saves once, so everything
/// is committed in one transaction (or not at all).
/// </summary>
public sealed class ClinicStatusChanger(AppDbContext db, AppInfo app)
{
    public void Apply(Clinic clinic, ClinicStatus newStatus, string? message, Guid administratorId, DateTime now, bool bulk = false)
    {
        var previous = clinic.Status;
        clinic.Status = newStatus;
        clinic.StatusMessage = message;
        clinic.StatusChangedAt = now;
        clinic.UpdatedAt = now;

        db.ClinicStatusChanges.Add(new ClinicStatusChange
        {
            Id = Guid.CreateVersion7(),
            ClinicId = clinic.Id,
            FromStatus = previous,
            ToStatus = newStatus,
            AdministratorId = administratorId,
            ChangedAt = now,
            Reason = message,
        });
        db.Record(ActivityEventType.ClinicStatusChanged, now, clinic.Id, administratorId,
            $"{previous}→{newStatus}{(bulk ? ";bulk" : "")}");
        db.EnqueueEmail(OutboundEmailKind.StatusChanged, clinic,
            EmailTemplates.StatusChanged(app.Name, clinic, message, app.ClinicLoginUrl), now);
    }
}
