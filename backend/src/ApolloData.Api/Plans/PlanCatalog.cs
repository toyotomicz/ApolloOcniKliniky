namespace ApolloData.Api.Plans;

/// <summary>Groups of additional registration fields that a plan can require.</summary>
public static class PlanFieldGroups
{
    /// <summary>Dedicated operational contact (name, email, optional phone).</summary>
    public const string OperationalContact = "operationalContact";

    /// <summary>Service configuration (number of workplaces, onboarding preference, technical contact).</summary>
    public const string ServiceConfiguration = "serviceConfiguration";
}

public sealed record PlanDefinition(
    string Code,
    string Name,
    string Tagline,
    string Description,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> FieldGroups,
    bool Highlighted,
    string? Badge)
{
    public bool Requires(string fieldGroup) => FieldGroups.Contains(fieldGroup);
}

/// <summary>
/// Single source of truth for registration plans. The frontend renders plan cards and
/// plan-specific registration steps from <c>GET /api/plans</c>, and the backend validates against it.
/// <para>
/// PROVISIONAL CONTENT: plan names, descriptions and features were not specified by the assignment.
/// They describe what the demo application actually does and contain no prices or commercial promises.
/// Edit this file to change them.
/// </para>
/// </summary>
public static class PlanCatalog
{
    public const string Basic = "basic";
    public const string Standard = "standard";
    public const string Premium = "premium";

    public static IReadOnlyList<PlanDefinition> All { get; } =
    [
        new(
            Code: Basic,
            Name: "Basic",
            Tagline: "Základní registrace oční ambulance",
            Description: "Pro oční ambulance s jedním pracovištěm, které potřebují evidovat základní údaje a kontaktní osobu.",
            Features:
            [
                "Registrace oční kliniky a kontaktní osoby",
                "Přihlášení jednorázovým kódem z e-mailu",
                "Přehled registrace v portálu kliniky",
                "Úprava kontaktních údajů",
            ],
            FieldGroups: [],
            Highlighted: false,
            Badge: null),
        new(
            Code: Standard,
            Name: "Standard",
            Tagline: "Registrace s provozním kontaktem",
            Description: "Pro oční kliniky, které mají vyhrazenou osobu pro provozní komunikaci.",
            Features:
            [
                "Vše z plánu Basic",
                "Samostatný provozní kontakt",
                "Provozní kontakt viditelný v přehledu kliniky",
            ],
            FieldGroups: [PlanFieldGroups.OperationalContact],
            Highlighted: true,
            Badge: "Doporučený"),
        new(
            Code: Premium,
            Name: "Premium",
            Tagline: "Registrace s konfigurací služby",
            Description: "Pro oční centra s více pracovišti a vlastním technickým kontaktem.",
            Features:
            [
                "Vše z plánu Standard",
                "Konfigurace podle počtu pracovišť",
                "Volba formy úvodního nastavení (online / na místě)",
                "Technický kontakt pro správu IT",
            ],
            FieldGroups: [PlanFieldGroups.OperationalContact, PlanFieldGroups.ServiceConfiguration],
            Highlighted: false,
            Badge: null),
    ];

    public static PlanDefinition? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(p => string.Equals(p.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
