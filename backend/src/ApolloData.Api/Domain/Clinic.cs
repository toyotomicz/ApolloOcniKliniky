namespace ApolloData.Api.Domain;

/// <summary>Administrative approval state of a clinic registration.</summary>
public enum ClinicStatus
{
    Pending,
    Approved,
    Rejected,
}

public enum OnboardingPreference
{
    Online,
    OnSite,
}

/// <summary>A registered clinic. Created in <see cref="ClinicStatus.Pending"/> state by public registration.</summary>
public sealed class Clinic
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Optional Czech company identifier (IČO), 8 digits.</summary>
    public string? OrganizationId { get; set; }

    public required string ContactFirstName { get; set; }
    public required string ContactLastName { get; set; }

    /// <summary>Email exactly as entered (trimmed); used for delivery.</summary>
    public required string Email { get; set; }

    /// <summary>Lower-cased email used for lookups and the unique constraint.</summary>
    public required string NormalizedEmail { get; set; }

    public required string Phone { get; set; }
    public required string Street { get; set; }
    public required string City { get; set; }
    public required string PostalCode { get; set; }

    /// <summary>Code of a plan from <see cref="Plans.PlanCatalog"/>.</summary>
    public required string PlanCode { get; set; }

    /// <summary>Booking contacts published for patients (normalized; phones without spaces).</summary>
    public List<string> PublicEmails { get; set; } = [];
    public List<string> PublicPhones { get; set; } = [];
    public string? Website { get; set; }

    /// <summary>Further information for patients, e.g. opening hours or how to book.</summary>
    public string? PatientInfo { get; set; }

    /// <summary>
    /// When the registering person confirmed they are authorized to act on behalf of the organization.
    /// Null for registrations created before the confirmation existed.
    /// </summary>
    public DateTime? AuthorityConfirmedAt { get; set; }

    /// <summary>Required by the Standard and Premium plans.</summary>
    public OperationalContact? OperationalContact { get; set; }

    /// <summary>Required by the Premium plan.</summary>
    public ServiceConfiguration? ServiceConfiguration { get; set; }

    public ClinicStatus Status { get; set; } = ClinicStatus.Pending;

    /// <summary>
    /// Explanation of the current status that is intentionally visible to the clinic
    /// (e.g. a rejection reason). Internal administrator notes are stored separately in <see cref="AdminNote"/>.
    /// </summary>
    public string? StatusMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? StatusChangedAt { get; set; }

    public DateTime PrivacyNoticeAcceptedAt { get; set; }
    public required string PrivacyNoticeVersion { get; set; }

    /// <summary>WGS84 coordinates resolved from the address by the background geocoder (null until resolved).</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>Time of the last geocoding attempt. Reset when the address changes so it is resolved again.</summary>
    public DateTime? GeocodedAt { get; set; }

    /// <summary>Created by the optional demo-data command. Demo clinics never receive emails.</summary>
    public bool IsDemo { get; set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL's <c>xmin</c> system column.</summary>
    public uint Version { get; set; }

    public List<ClinicStatusChange> StatusChanges { get; } = [];
    public List<AdminNote> Notes { get; } = [];
    public List<ClinicMessage> Messages { get; } = [];

    /// <summary>Clears the resolved location after an address change; the geocoder then resolves it again.</summary>
    public void ResetLocation()
    {
        Latitude = null;
        Longitude = null;
        GeocodedAt = null;
    }
}

public sealed class OperationalContact
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
}

public sealed class ServiceConfiguration
{
    public int WorkplaceCount { get; set; }
    public OnboardingPreference OnboardingPreference { get; set; }
    public string? TechnicalContactEmail { get; set; }
    public string? Notes { get; set; }
}
