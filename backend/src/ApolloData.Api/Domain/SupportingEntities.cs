namespace ApolloData.Api.Domain;

/// <summary>
/// A one-time login code issued to a clinic. Only an HMAC of the code is stored.
/// A challenge is usable when it was delivered, is not consumed, invalidated or expired,
/// and has attempts remaining.
/// </summary>
public sealed class ClinicLoginChallenge
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Clinic? Clinic { get; set; }

    public required byte[] CodeHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set once SMTP accepted the message. Undelivered challenges can never be used.</summary>
    public DateTime? DeliveredAt { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? ConsumedAt { get; set; }

    /// <summary>Set when superseded by a newer code or when delivery failed.</summary>
    public DateTime? InvalidatedAt { get; set; }
}

public sealed class Administrator
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
}

public enum SessionKind
{
    Clinic,
    Administrator,
}

/// <summary>Server-side session. The auth cookie only carries the session id, so sessions can be revoked.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; }
    public SessionKind Kind { get; set; }
    public Guid? ClinicId { get; set; }
    public Guid? AdministratorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Audit record of every administrative status change.</summary>
public sealed class ClinicStatusChange
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public ClinicStatus FromStatus { get; set; }
    public ClinicStatus ToStatus { get; set; }
    public Guid AdministratorId { get; set; }
    public Administrator? Administrator { get; set; }
    public DateTime ChangedAt { get; set; }

    /// <summary>Message shown to the clinic together with the new status.</summary>
    public string? Reason { get; set; }
}

/// <summary>Internal note visible to administrators only.</summary>
public sealed class AdminNote
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid AdministratorId { get; set; }
    public Administrator? Administrator { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ActivityEventType
{
    ClinicRegistered,
    ClinicEmailVerified,
    ClinicSignedIn,
    ClinicProfileUpdated,
    ClinicStatusChanged,
    AdminNoteAdded,
    AdminSignedIn,
}

/// <summary>Append-only activity log used for the dashboard feed and "unique clinic sign-ins" metric.</summary>
public sealed class ActivityEvent
{
    public long Id { get; set; }
    public ActivityEventType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? ClinicId { get; set; }
    public Clinic? Clinic { get; set; }
    public Guid? AdministratorId { get; set; }
    public Administrator? Administrator { get; set; }

    /// <summary>Short machine-friendly detail, e.g. "Pending→Approved". Never contains secrets.</summary>
    public string? Detail { get; set; }
}
