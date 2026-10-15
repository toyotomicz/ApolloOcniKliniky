using System.Globalization;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ApolloData.Api.Documents;

/// <summary>
/// "Potvrzení o registraci" – a one-page PDF with the clinic's registration data and current status,
/// generated on request (nothing is stored). Rendered with QuestPDF (Community license, see README).
/// </summary>
public static class ConfirmationPdf
{
    private const string Ink = "#212529";
    private const string Muted = "#5b6570";
    private const string Border = "#e2e5e8";
    private const string Chrome = "#313336";
    private const string Accent = "#c93a32";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static string ReferenceNumber(Clinic clinic) => $"REG-{clinic.Id.ToString("N")[..10].ToUpperInvariant()}";

    public static string FileName(Clinic clinic) => $"potvrzeni-registrace-{ReferenceNumber(clinic)}.pdf";

    public static byte[] Generate(Clinic clinic, string appName, DateTime generatedAtUtc, TimeZoneInfo timeZone)
    {
        string Local(DateTime? utc) => utc is { } value
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value, DateTimeKind.Utc), timeZone).ToString("d. M. yyyy H:mm", Czech)
            : "—";

        var plan = PlanCatalog.Find(clinic.PlanCode);
        var (statusLabel, statusColor) = clinic.Status switch
        {
            ClinicStatus.Approved => ("Schváleno", "#1d7348"),
            ClinicStatus.Rejected => ("Zamítnuto", "#b42318"),
            _ => ("Čeká na posouzení", "#8a5a00"),
        };

        var rows = new List<(string Label, string Value)>
        {
            ("Název kliniky", clinic.Name),
            ("IČO", clinic.OrganizationId ?? "—"),
            ("Adresa", $"{clinic.Street}, {FormatPostalCode(clinic.PostalCode)} {clinic.City}"),
            ("Kontaktní osoba", $"{clinic.ContactFirstName} {clinic.ContactLastName}"),
            ("E-mail", clinic.Email),
            ("Telefon", FormatPhone(clinic.Phone)),
            ("Plán", plan is null ? clinic.PlanCode : $"{plan.Name} – {plan.Tagline}"),
        };
        if (clinic.OperationalContact is { } oc)
        {
            rows.Add(("Provozní kontakt", $"{oc.Name}, {oc.Email}{(oc.Phone is null ? "" : $", {FormatPhone(oc.Phone)}")}"));
        }
        if (clinic.ServiceConfiguration is { } sc)
        {
            rows.Add(("Počet pracovišť", sc.WorkplaceCount.ToString(Czech)));
            rows.Add(("Úvodní nastavení", sc.OnboardingPreference == OnboardingPreference.OnSite ? "Na místě" : "Online"));
            if (sc.TechnicalContactEmail is not null) rows.Add(("Technický kontakt", sc.TechnicalContactEmail));
        }

        var registrationRows = new List<(string Label, string Value)>
        {
            ("Datum registrace", Local(clinic.CreatedAt)),
            ("Ověření e-mailu", clinic.EmailVerifiedAt is null ? "Zatím neověřen" : Local(clinic.EmailVerifiedAt)),
            ("Souhlas se zásadami", $"{Local(clinic.PrivacyNoticeAcceptedAt)} (verze {clinic.PrivacyNoticeVersion})"),
            ("Poslední změna stavu", Local(clinic.StatusChangedAt)),
        };

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink).LineHeight(1.35f));

            page.Header().Column(header =>
            {
                header.Item().Background(Chrome).PaddingHorizontal(40).PaddingVertical(18).Row(row =>
                {
                    row.RelativeItem().Column(brand =>
                    {
                        brand.Item().Text(appName).FontSize(16).Bold().FontColor(Colors.White);
                        brand.Item().Text("OČNÍ KLINIKY").FontSize(7.5f).LetterSpacing(0.18f).FontColor("#d3d3d3");
                    });
                    row.AutoItem().AlignMiddle().Text("Potvrzení o registraci").FontSize(10).FontColor("#d3d3d3");
                });
                header.Item().Height(3).Background(Accent);
            });

            page.Content().PaddingHorizontal(40).PaddingTop(28).PaddingBottom(16).Column(col =>
            {
                col.Spacing(14);
                col.Item().Text("Potvrzení o registraci kliniky").FontSize(20).Bold();
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("Číslo registrace: ").FontColor(Muted);
                        t.Span(ReferenceNumber(clinic)).SemiBold();
                    });
                    row.AutoItem().Background(statusColor).PaddingHorizontal(10).PaddingVertical(4)
                        .Text(statusLabel).FontSize(9).SemiBold().FontColor(Colors.White);
                });

                col.Item().PaddingTop(6).Text("Údaje o klinice").FontSize(12).SemiBold().FontColor("#465051");
                col.Item().Element(c => DataTable(c, rows));

                col.Item().PaddingTop(6).Text("Průběh registrace").FontSize(12).SemiBold().FontColor("#465051");
                col.Item().Element(c => DataTable(c, registrationRows));

                if (clinic.StatusMessage is { Length: > 0 } message)
                {
                    col.Item().Background("#f5f6f7").BorderLeft(3).BorderColor(statusColor).Padding(12).Column(box =>
                    {
                        box.Item().Text(clinic.Status == ClinicStatus.Rejected ? "DŮVOD ZAMÍTNUTÍ" : "ZPRÁVA OD ADMINISTRÁTORA")
                            .FontSize(8).SemiBold().LetterSpacing(0.06f).FontColor(Muted);
                        box.Item().PaddingTop(4).Text(message);
                    });
                }

                col.Item().PaddingTop(10).Text(
                        "Dokument potvrzuje přijetí registrace a její stav ke dni vystavení. Nejde o smlouvu ani o rozhodnutí " +
                        "o poskytování služeb. Aktuální stav registrace najdete v portálu kliniky.")
                    .FontSize(8.5f).FontColor(Muted);
            });

            page.Footer().PaddingHorizontal(40).PaddingBottom(22).BorderTop(0.5f).BorderColor(Border).PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text($"Vystaveno {Local(generatedAtUtc)} · {appName}").FontSize(8).FontColor(Muted);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                    t.Span("Strana ");
                    t.CurrentPageNumber();
                    t.Span(" z ");
                    t.TotalPages();
                });
            });
        }));

        document.WithMetadata(new DocumentMetadata
        {
            Title = $"Potvrzení o registraci – {clinic.Name}",
            Author = appName,
            Subject = ReferenceNumber(clinic),
            Creator = appName,
            CreationDate = new DateTimeOffset(DateTime.SpecifyKind(generatedAtUtc, DateTimeKind.Utc)),
        });
        return document.GeneratePdf();
    }

    private static void DataTable(IContainer container, IReadOnlyList<(string Label, string Value)> rows) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(150);
                c.RelativeColumn();
            });
            foreach (var (label, value) in rows)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(6).Text(label).FontColor(Muted);
                table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(6).Text(value);
            }
        });

    /// <summary>"+420777123456" → "+420 777 123 456" (stored without spaces).</summary>
    public static string FormatPhone(string phone)
    {
        var prefix = phone.StartsWith("+420", StringComparison.Ordinal) ? "+420 " : "";
        var digits = prefix.Length > 0 ? phone[4..] : phone;
        return digits.Length == 9 && digits.All(char.IsAsciiDigit)
            ? $"{prefix}{digits[..3]} {digits[3..6]} {digits[6..]}"
            : phone;
    }

    private static string FormatPostalCode(string postalCode) =>
        postalCode.Length == 5 && postalCode.All(char.IsDigit) ? $"{postalCode[..3]} {postalCode[3..]}" : postalCode;
}
