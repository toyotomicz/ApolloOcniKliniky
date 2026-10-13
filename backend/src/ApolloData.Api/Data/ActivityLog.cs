using ApolloData.Api.Domain;

namespace ApolloData.Api.Data;

public static class ActivityLog
{
    /// <summary>Adds an activity event to the current unit of work (saved with the surrounding <c>SaveChanges</c>).</summary>
    public static void Record(this AppDbContext db, ActivityEventType type, DateTime occurredAt,
        Guid? clinicId = null, Guid? administratorId = null, string? detail = null) =>
        db.ActivityEvents.Add(new ActivityEvent
        {
            Type = type,
            OccurredAt = occurredAt,
            ClinicId = clinicId,
            AdministratorId = administratorId,
            Detail = detail,
        });
}
