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

    /// <summary>Time-based one-time passwords (RFC 6238, e.g. Google Authenticator) are required after the password.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Confirmed TOTP secret (Base32), encrypted with ASP.NET Core Data Protection.</summary>
    public string? TwoFactorSecret { get; set; }

    /// <summary>Secret generated during setup; becomes <see cref="TwoFactorSecret"/> once a valid code confirms it.</summary>
    public string? TwoFactorPendingSecret { get; set; }

    public DateTime? TwoFactorEnabledAt { get; set; }

    /// <summary>Last accepted TOTP time step. Codes from this or an earlier step are rejected (no replay).</summary>
    public long? TwoFactorLastTimeStep { get; set; }
}

/// <summary>Single-use recovery code for an administrator with two-factor authentication. Only a hash is stored.</summary>
public sealed class AdminRecoveryCode
{
    public Guid Id { get; set; }
    public Guid AdministratorId { get; set; }
    public required byte[] CodeHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UsedAt { get; set; }
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

public enum MessageSender
{
    Clinic,
    Administrator,
}

/// <summary>Message in the conversation between a clinic and the administrators.</summary>
public sealed class ClinicMessage
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public MessageSender Sender { get; set; }

    /// <summary>Author when <see cref="Sender"/> is <see cref="MessageSender.Administrator"/>.</summary>
    public Guid? AdministratorId { get; set; }
    public Administrator? Administrator { get; set; }

    public required string Body { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>When the other side (clinic or any administrator) first read the message.</summary>
    public DateTime? ReadAt { get; set; }
}

public enum OutboundEmailKind
{
    StatusChanged,
    NewMessage,
}

/// <summary>
/// Transactional outbox for notification emails: rows are written in the same transaction as the change
/// that triggers them and delivered by a background worker with retries, so an SMTP outage loses nothing.
/// </summary>
public sealed class OutboundEmail
{
    public Guid Id { get; set; }
    public OutboundEmailKind Kind { get; set; }
    public Guid? ClinicId { get; set; }
    public required string ToEmail { get; set; }
    public string? ToName { get; set; }
    public required string Subject { get; set; }
    public required string TextBody { get; set; }
    public required string HtmlBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? SentAt { get; set; }

    /// <summary>Set when delivery was given up after the maximum number of attempts.</summary>
    public DateTime? FailedAt { get; set; }

    /// <summary>Safe diagnostic of the last failure (exception type / SMTP status); never credentials.</summary>
    public string? LastError { get; set; }
}

public enum ActivityEventType
{
    ClinicRegistered,
    ClinicEmailVerified,
    ClinicSignedIn,
    ClinicSignedOut,
    ClinicProfileUpdated,
    ClinicStatusChanged,
    ClinicMessageSent,
    ConfirmationDownloaded,
    AdminNoteAdded,
    AdminSignedIn,
    AdminSignedOut,
    AdminSignInFailed,
    AdminMessageSent,
    AdminTwoFactorEnabled,
    AdminTwoFactorDisabled,
    AdminRecoveryCodesRenewed,
    AdminPasswordReset,
    AuditLogExported,
    EmailSent,
    EmailFailed,
    DemoDataSeeded,
    DemoDataRemoved,
    ClinicDeleted,
}

/// <summary>
/// Append-only audit log of every state-changing action (who, what, when, on which clinic).
/// Feeds the dashboard activity, the "unique clinic sign-ins" metric and the downloadable audit log.
/// </summary>
public sealed class ActivityEvent
{
    public long Id { get; set; }
    public ActivityEventType Type { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? ClinicId { get; set; }
    public Clinic? Clinic { get; set; }
    public Guid? AdministratorId { get; set; }
    public Administrator? Administrator { get; set; }

    /// <summary>Short machine-friendly detail, e.g. "Pending→Approved" or "Pending→Approved;bulk". Never contains secrets.</summary>
    public string? Detail { get; set; }
}
