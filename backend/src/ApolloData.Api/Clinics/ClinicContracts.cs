using ApolloData.Api.Common;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;

namespace ApolloData.Api.Clinics;

public sealed record OperationalContactInput(string? Name, string? Email, string? Phone);

public sealed record ServiceConfigurationInput(
    int? WorkplaceCount,
    OnboardingPreference? OnboardingPreference,
    string? TechnicalContactEmail,
    string? Notes);

public sealed record RegistrationRequest(
    string? PlanCode,
    string? Name,
    string? OrganizationId,
    string? ContactFirstName,
    string? ContactLastName,
    string? Email,
    string? Phone,
    string? Street,
    string? City,
    string? PostalCode,
    OperationalContactInput? OperationalContact,
    ServiceConfigurationInput? ServiceConfiguration,
    bool PrivacyNoticeAccepted);

/// <summary>
/// Fields a clinic may change itself. Name, IČO, email and plan are intentionally not editable
/// by the clinic (email is the login identity; the rest is part of the administrative review).
/// </summary>
public sealed record UpdateClinicProfileRequest(
    string? ContactFirstName,
    string? ContactLastName,
    string? Phone,
    string? Street,
    string? City,
    string? PostalCode,
    OperationalContactInput? OperationalContact,
    ServiceConfigurationInput? ServiceConfiguration);

/// <summary>Validated, normalized contact data shared by registration and profile updates.</summary>
public sealed record ValidatedContactData(
    string ContactFirstName,
    string ContactLastName,
    string Phone,
    string Street,
    string City,
    string PostalCode,
    OperationalContact? OperationalContact,
    ServiceConfiguration? ServiceConfiguration);

public sealed record ValidatedRegistration(
    PlanDefinition Plan,
    string Name,
    string? OrganizationId,
    string Email,
    ValidatedContactData Contact);

public static class ClinicValidator
{
    public const int MaxWorkplaces = 500;

    public static (ValidatedRegistration? Result, ValidationErrors Errors) ValidateRegistration(RegistrationRequest request)
    {
        var errors = new ValidationErrors();

        var plan = PlanCatalog.Find(request.PlanCode);
        if (plan is null) errors.Add("planCode", "Vyberte jeden z nabízených plánů.");

        var name = Rules.Required(errors, "name", request.Name, 200, minLength: 2);
        var organizationId = Rules.OrganizationId(errors, "organizationId", request.OrganizationId);
        var email = Rules.Email(errors, "email", request.Email);

        var contact = ValidateContact(errors, plan,
            request.ContactFirstName, request.ContactLastName, request.Phone,
            request.Street, request.City, request.PostalCode,
            request.OperationalContact, request.ServiceConfiguration);

        if (!request.PrivacyNoticeAccepted)
        {
            errors.Add("privacyNoticeAccepted", "Pro odeslání registrace je nutné potvrdit seznámení se zásadami zpracování údajů.");
        }

        if (!errors.IsValid) return (null, errors);
        return (new ValidatedRegistration(plan!, name!, organizationId, email!, contact!), errors);
    }

    public static (ValidatedContactData? Result, ValidationErrors Errors) ValidateProfileUpdate(UpdateClinicProfileRequest request, PlanDefinition plan)
    {
        var errors = new ValidationErrors();
        var contact = ValidateContact(errors, plan,
            request.ContactFirstName, request.ContactLastName, request.Phone,
            request.Street, request.City, request.PostalCode,
            request.OperationalContact, request.ServiceConfiguration);
        return (errors.IsValid ? contact : null, errors);
    }

    private static ValidatedContactData? ValidateContact(
        ValidationErrors errors,
        PlanDefinition? plan,
        string? firstName, string? lastName, string? phone,
        string? street, string? city, string? postalCode,
        OperationalContactInput? operational, ServiceConfigurationInput? service)
    {
        var first = Rules.Required(errors, "contactFirstName", firstName, 100);
        var last = Rules.Required(errors, "contactLastName", lastName, 100);
        var normalizedPhone = Rules.Phone(errors, "phone", phone);
        var normalizedStreet = Rules.Required(errors, "street", street, 200);
        var normalizedCity = Rules.Required(errors, "city", city, 100);
        var normalizedPostal = Rules.PostalCode(errors, "postalCode", postalCode);

        // Plan-specific groups are validated only when the plan requires them.
        // Data for groups that the plan does not include is ignored (never persisted).
        OperationalContact? operationalContact = null;
        if (plan?.Requires(PlanFieldGroups.OperationalContact) == true)
        {
            operationalContact = ValidateOperationalContact(errors, operational);
        }

        ServiceConfiguration? serviceConfiguration = null;
        if (plan?.Requires(PlanFieldGroups.ServiceConfiguration) == true)
        {
            serviceConfiguration = ValidateServiceConfiguration(errors, service);
        }

        if (!errors.IsValid) return null;
        return new ValidatedContactData(first!, last!, normalizedPhone!, normalizedStreet!, normalizedCity!, normalizedPostal!,
            operationalContact, serviceConfiguration);
    }

    private static OperationalContact? ValidateOperationalContact(ValidationErrors errors, OperationalContactInput? input)
    {
        const string prefix = PlanFieldGroups.OperationalContact;
        var name = Rules.Required(errors, $"{prefix}.name", input?.Name, 200);
        var email = Rules.Email(errors, $"{prefix}.email", input?.Email);
        var phone = Rules.Phone(errors, $"{prefix}.phone", input?.Phone, required: false);
        return name is null || email is null ? null : new OperationalContact { Name = name, Email = email, Phone = phone };
    }

    private static ServiceConfiguration? ValidateServiceConfiguration(ValidationErrors errors, ServiceConfigurationInput? input)
    {
        const string prefix = PlanFieldGroups.ServiceConfiguration;
        var valid = true;

        if (input?.WorkplaceCount is not { } count)
        {
            errors.Add($"{prefix}.workplaceCount", "Zadejte počet pracovišť.");
            valid = false;
        }
        else if (count is < 1 or > MaxWorkplaces)
        {
            errors.Add($"{prefix}.workplaceCount", $"Počet pracovišť musí být mezi 1 a {MaxWorkplaces}.");
            valid = false;
        }

        if (input?.OnboardingPreference is not { } preference || !Enum.IsDefined(preference))
        {
            errors.Add($"{prefix}.onboardingPreference", "Vyberte preferovanou formu úvodního nastavení.");
            valid = false;
        }

        var technicalEmail = Rules.Email(errors, $"{prefix}.technicalContactEmail", input?.TechnicalContactEmail, required: false);
        if (input?.TechnicalContactEmail is { Length: > 0 } && technicalEmail is null) valid = false;

        var notes = Rules.Optional(errors, $"{prefix}.notes", input?.Notes, 1000);
        if (input?.Notes is { Length: > 1000 }) valid = false;

        return valid
            ? new ServiceConfiguration
            {
                WorkplaceCount = input!.WorkplaceCount!.Value,
                OnboardingPreference = input.OnboardingPreference!.Value,
                TechnicalContactEmail = technicalEmail,
                Notes = notes,
            }
            : null;
    }
}

public sealed record PlanSummaryDto(string Code, string Name, string Tagline, IReadOnlyList<string> Features);

public sealed record OperationalContactDto(string Name, string Email, string? Phone);

public sealed record ServiceConfigurationDto(int WorkplaceCount, OnboardingPreference OnboardingPreference, string? TechnicalContactEmail, string? Notes);

/// <summary>Clinic data that is visible to the clinic itself (and also shown to administrators).</summary>
public sealed record ClinicProfileDto(
    Guid Id,
    string Name,
    string? OrganizationId,
    string ContactFirstName,
    string ContactLastName,
    string Email,
    string Phone,
    string Street,
    string City,
    string PostalCode,
    PlanSummaryDto Plan,
    OperationalContactDto? OperationalContact,
    ServiceConfigurationDto? ServiceConfiguration,
    ClinicStatus Status,
    string? StatusMessage,
    DateTime? StatusChangedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? EmailVerifiedAt)
{
    public static ClinicProfileDto From(Clinic clinic)
    {
        var plan = PlanCatalog.Find(clinic.PlanCode);
        var planDto = plan is null
            ? new PlanSummaryDto(clinic.PlanCode, clinic.PlanCode, "", [])
            : new PlanSummaryDto(plan.Code, plan.Name, plan.Tagline, plan.Features.Where(f => f.Included).Select(f => f.Text).ToList());

        return new ClinicProfileDto(
            clinic.Id, clinic.Name, clinic.OrganizationId,
            clinic.ContactFirstName, clinic.ContactLastName, clinic.Email, clinic.Phone,
            clinic.Street, clinic.City, clinic.PostalCode,
            planDto,
            clinic.OperationalContact is { } oc ? new OperationalContactDto(oc.Name, oc.Email, oc.Phone) : null,
            clinic.ServiceConfiguration is { } sc
                ? new ServiceConfigurationDto(sc.WorkplaceCount, sc.OnboardingPreference, sc.TechnicalContactEmail, sc.Notes)
                : null,
            clinic.Status, clinic.StatusMessage, clinic.StatusChangedAt,
            clinic.CreatedAt, clinic.UpdatedAt, clinic.EmailVerifiedAt);
    }
}
