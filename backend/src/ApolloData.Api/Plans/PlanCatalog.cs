namespace ApolloData.Api.Plans;

/// <summary>Groups of additional registration fields that a plan can require.</summary>
public static class PlanFieldGroups
{
    /// <summary>Dedicated operational contact (name, email, optional phone).</summary>
    public const string OperationalContact = "operationalContact";

    /// <summary>Service configuration (number of workplaces, onboarding preference, technical contact).</summary>
    public const string ServiceConfiguration = "serviceConfiguration";
}

/// <summary>One row of the plan comparison; <see cref="Included"/> false is shown as "not part of this level".</summary>
public sealed record PlanFeature(string Text, bool Included = true);

public sealed record PlanDefinition(
    string Code,
    string Name,
    string Tagline,
    string Description,
    IReadOnlyList<PlanFeature> Features,
    IReadOnlyList<string> FieldGroups,
    bool Highlighted,
    string? Badge)
{
    public bool Requires(string fieldGroup) => FieldGroups.Contains(fieldGroup);
}

/// <summary>
/// Single source of truth for the registration levels. The frontend renders plan cards and level-specific
/// registration steps from <c>GET /api/plans</c>, and the backend validates against it.
/// Texts (names, prices, features) come from the business specification; the codes basic/standard/premium are
/// internal identifiers stored with each clinic and must not change.
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
            Name: "Úroveň 1",
            Tagline: "Úplně zdarma, pouze kontaktní informace",
            Description: "Zcela zdarma",
            Features:
            [
                new("Cena: Zcela zdarma"),
                new("Funkce: Zobrazení zařízení v seznamu partnerů včetně telefonu, e-mailu a webu"),
                new("Přímá rezervace v aplikaci: Není dostupná", Included: false),
                new("Nastavení kapacity: Není součástí", Included: false),
                new("Možnosti zprovoznění konektivity: Není součástí", Included: false),
            ],
            FieldGroups: [],
            Highlighted: false,
            Badge: null),
        new(
            Code: Standard,
            Name: "Úroveň 2",
            Tagline: "Offline rezervační requesty s výsledkem testu",
            Description: "50 Kč za každý zaslaný požadavek, prvních 5 požadavků za měsíc zdarma",
            Features:
            [
                new("Cena: 50 Kč za každý zaslaný požadavek, prvních 5 požadavků za měsíc zdarma"),
                new("Funkce: Pojištěnec odešle požadavek na zpětné kontaktování včetně výsledků orientačních testů"),
                new("Přímá rezervace v aplikaci: Není dostupná", Included: false),
                new("Nastavení kapacity: Možnost průběžně nastavovat maximální počet přijatých požadavků"),
                new("Možnosti zprovoznění konektivity: Není součástí", Included: false),
            ],
            FieldGroups: [PlanFieldGroups.OperationalContact],
            Highlighted: false,
            Badge: null),
        new(
            Code: Premium,
            Name: "Úroveň 3",
            Tagline: "Online rezervace + pokročilé integrační varianty",
            Description: "100 Kč za každou úspěšně provedenou a API potvrzenou rezervaci",
            Features:
            [
                new("Cena: 100 Kč za každou úspěšně provedenou a API potvrzenou rezervaci"),
                new("Funkce: Pojištěnec si vybere volný slot přímo ve vašem kalendáři"),
                new("Přímá rezervace v aplikaci: Dostupná přes API integraci"),
                new("Nastavení kapacity: Řídí váš rezervační systém"),
                new("Možnosti zprovoznění konektivity: Standardní dle technické dokumentace nebo individuální úprava komunikačního rozhraní"),
            ],
            FieldGroups: [PlanFieldGroups.OperationalContact, PlanFieldGroups.ServiceConfiguration],
            Highlighted: false,
            Badge: null),
    ];

    public static PlanDefinition? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(p => string.Equals(p.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}
