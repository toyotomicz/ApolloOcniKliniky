using ApolloData.Api.Clinics;
using ApolloData.Api.Common;
using ApolloData.Api.Data;
using ApolloData.Api.Domain;
using ApolloData.Api.Plans;
using Microsoft.EntityFrameworkCore;

namespace ApolloData.Api.Hosting;

/// <summary>
/// Optional demo data for presentations: realistic eye clinics across the Czech Republic with status history,
/// notes, conversations and activity, spread over the last weeks. Everything is marked <see cref="Clinic.IsDemo"/>,
/// uses the reserved .test domain (undeliverable) and never queues email. Run with <c>seed-demo-data</c>;
/// <c>--remove</c> deletes the demo data again, <c>--reset</c> recreates it.
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, TimeProvider time)
{
    public const string EmailDomain = "demo.apollodata.test";
    public const int ClinicCount = 45;

    private sealed record City(string Name, string PostalCode, double Latitude, double Longitude);

    private static readonly City[] Cities =
    [
        new("Praha", "110 00", 50.0755, 14.4378), new("Praha", "120 00", 50.0736, 14.4369), new("Praha", "160 00", 50.1008, 14.3938),
        new("Brno", "602 00", 49.1951, 16.6068), new("Brno", "612 00", 49.2103, 16.5923), new("Ostrava", "702 00", 49.8209, 18.2625),
        new("Plzeň", "301 00", 49.7384, 13.3736), new("Liberec", "460 01", 50.7663, 15.0543), new("Olomouc", "779 00", 49.5938, 17.2509),
        new("České Budějovice", "370 01", 48.9745, 14.4743), new("Hradec Králové", "500 02", 50.2104, 15.8252),
        new("Ústí nad Labem", "400 01", 50.6607, 14.0323), new("Pardubice", "530 02", 50.0343, 15.7812), new("Zlín", "760 01", 49.2265, 17.6707),
        new("Kladno", "272 01", 50.1473, 14.1029), new("Opava", "746 01", 49.9387, 17.9026), new("Jihlava", "586 01", 49.3961, 15.5912),
        new("Karlovy Vary", "360 01", 50.2319, 12.8720), new("Teplice", "415 01", 50.6404, 13.8245), new("Mladá Boleslav", "293 01", 50.4113, 14.9032),
        new("Tábor", "390 01", 49.4144, 14.6578), new("Znojmo", "669 02", 48.8555, 16.0488), new("Kolín", "280 02", 50.0281, 15.2006),
        new("Písek", "397 01", 49.3088, 14.1475), new("Trutnov", "541 01", 50.5610, 15.9127), new("Třebíč", "674 01", 49.2149, 15.8817),
        new("Přerov", "750 02", 49.4551, 17.4509), new("Prostějov", "796 01", 49.4719, 17.1118), new("Děčín", "405 02", 50.7726, 14.2128),
        new("Havlíčkův Brod", "580 01", 49.6078, 15.5807),
    ];

    private static readonly string[] Streets =
        ["Masarykova", "Nádražní", "Palackého", "Husova", "Smetanova", "Komenského", "Riegrova", "Tyršova", "Jiráskova", "Náměstí Míru", "Lidická", "Purkyňova"];

    private static readonly string[] Brands =
        ["Lumen", "Iris", "Vizus", "Optimum", "Fokus", "Horizont", "Spektrum", "Visio", "Jasný pohled", "Duhovka", "Optika Plus", "Ostrý zrak", "Panorama", "Zornice", "Okulus"];

    private static readonly (string[] First, string[] Last)[] People =
    [
        (["Jana", "Lucie", "Eva", "Kateřina", "Tereza", "Barbora"], ["Nováková", "Dvořáková", "Procházková", "Veselá", "Němcová", "Pokorná", "Hájková", "Jelínková"]),
        (["Petr", "Tomáš", "Martin", "Jakub", "Michal", "Ondřej"], ["Svoboda", "Černý", "Kučera", "Horák", "Marek", "Pospíšil", "Král", "Novák"]),
    ];

    private static (string First, string Last) RandomPerson(Random random)
    {
        var (first, last) = People[random.Next(People.Length)];
        return (first[random.Next(first.Length)], last[random.Next(last.Length)]);
    }

    private static readonly string[] RejectionReasons =
    [
        "Chybí doložení oprávnění k poskytování zdravotních služeb v oboru oftalmologie. Doplňte jej prosím a napište nám.",
        "IČO neodpovídá názvu kliniky v obchodním rejstříku. Opravte prosím údaje.",
        "Adresa provozovny je neúplná – doplňte prosím číslo popisné a kontaktujte nás.",
    ];

    private static readonly string[] ApprovalMessages =
    [
        "Vítejte! Úvodní nastavení naplánujeme v nejbližších dnech.",
        "Registrace je v pořádku. Ozveme se s termínem školení.",
    ];

    private static readonly string[] Notes =
    [
        "Telefonicky ověřeno s vedoucí lékařkou, údaje sedí.",
        "Oprávnění k poskytování zdravotních služeb doloženo e-mailem.",
        "Klinika plánuje rozšíření o druhé pracoviště – zvážit plán Premium.",
        "Kontaktní osoba je k zastižení jen dopoledne.",
    ];

    private static readonly (string Clinic, string? Admin)[] Conversations =
    [
        ("Dobrý den, můžeme v registraci později změnit plán na Premium?", "Dobrý den, ano – stačí nám napsat a plán upravíme."),
        ("Posíláme doplněné oprávnění, prosíme o kontrolu.", "Děkujeme, oprávnění máme, registraci posoudíme do dvou dnů."),
        ("Kdy můžeme očekávat výsledek posouzení?", null),
        ("Změnila se nám kontaktní osoba, údaje jsme upravili v portálu.", "Díky, změnu vidíme."),
        ("Je možné úvodní nastavení provést až v příštím měsíci?", null),
    ];

    public async Task<int> CountAsync(CancellationToken ct) => await db.Clinics.CountAsync(c => c.IsDemo, ct);

    public async Task<int> RemoveAsync(CancellationToken ct)
    {
        // Entities tracked from an earlier seed in this scope carry stale concurrency versions; load fresh ones.
        db.ChangeTracker.Clear();
        var ids = await db.Clinics.Where(c => c.IsDemo).Select(c => c.Id).ToListAsync(ct);
        if (ids.Count == 0) return 0;

        // Audit events would otherwise remain with a NULL clinic; demo history is removed with the demo clinics.
        await db.ActivityEvents.Where(e => e.ClinicId != null && ids.Contains(e.ClinicId.Value)).ExecuteDeleteAsync(ct);
        // History, notes, messages and sessions are removed by the database's ON DELETE CASCADE.
        db.Clinics.RemoveRange(await db.Clinics.Where(c => c.IsDemo).ToListAsync(ct));
        db.Record(ActivityEventType.DemoDataRemoved, time.GetUtcNow().UtcDateTime, detail: $"clinics={ids.Count}");
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var admin = await db.Administrators.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No administrator exists. Start the application once (it creates the administrator) first.");

        var random = new Random(20261014);
        var now = time.GetUtcNow().UtcDateTime;

        // An event that would lie in the future is placed at a random past moment after `notBefore`
        // (instead of all of them landing exactly on "now", which looks artificial in the audit log).
        DateTime Past(DateTime candidate, DateTime notBefore)
        {
            if (candidate < now) return candidate;
            var minutes = (now.AddMinutes(-2) - notBefore).TotalMinutes;
            return minutes <= 1 ? Min(notBefore.AddMinutes(1), now) : notBefore.AddMinutes(1 + random.NextDouble() * (minutes - 1));
        }
        var usedEmails = new HashSet<string>();

        for (var i = 0; i < ClinicCount; i++)
        {
            var city = Cities[i % Cities.Length];
            var brand = Brands[random.Next(Brands.Length)];
            var (contactFirst, surname) = RandomPerson(random);
            var name = random.Next(6) switch
            {
                0 => $"Oční klinika {brand} {city.Name}",
                1 => $"Oční centrum {brand}",
                2 => $"Oftalmologie {brand} s.r.o.",
                3 => $"Oční ambulance MUDr. {surname}",
                4 => $"Centrum refrakční chirurgie {brand}",
                _ => $"Oční studio {brand} {city.Name}",
            };

            var slug = Slug(name);
            var email = $"{slug}@{EmailDomain}";
            for (var n = 2; !usedEmails.Add(email); n++) email = $"{slug}-{n}@{EmailDomain}";

            // More registrations in recent days, a long tail over ~6 weeks.
            var ageDays = Math.Pow(random.NextDouble(), 1.6) * 44;
            var createdAt = now.AddDays(-ageDays).AddMinutes(-random.Next(0, 600));
            var planCode = random.Next(10) switch { < 4 => PlanCatalog.Basic, < 8 => PlanCatalog.Standard, _ => PlanCatalog.Premium };
            var plan = PlanCatalog.Find(planCode)!;

            var clinic = new Clinic
            {
                Id = Guid.CreateVersion7(new DateTimeOffset(createdAt, TimeSpan.Zero)),
                Name = name,
                OrganizationId = random.Next(5) == 0 ? null : RandomIco(random),
                ContactFirstName = contactFirst,
                ContactLastName = surname,
                Email = email,
                NormalizedEmail = email,
                Phone = $"+420{random.Next(601, 799)}{random.Next(100, 999)}{random.Next(100, 999)}",
                Street = $"{Streets[random.Next(Streets.Length)]} {random.Next(1, 120)}",
                City = city.Name,
                PostalCode = city.PostalCode,
                PlanCode = planCode,
                Status = ClinicStatus.Pending,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                PrivacyNoticeAcceptedAt = createdAt,
                PrivacyNoticeVersion = PublicEndpoints.PrivacyNoticeVersion,
                Latitude = city.Latitude + (random.NextDouble() - 0.5) * 0.03,
                Longitude = city.Longitude + (random.NextDouble() - 0.5) * 0.045,
                GeocodedAt = createdAt,
                IsDemo = true,
            };
            if (plan.Requires(PlanFieldGroups.OperationalContact))
            {
                var (operationalFirst, operationalLast) = RandomPerson(random);
                clinic.OperationalContact = new OperationalContact
                {
                    Name = $"{operationalFirst} {operationalLast}",
                    Email = $"provoz.{slug}@{EmailDomain}",
                    Phone = $"+420{random.Next(601, 799)}{random.Next(100, 999)}{random.Next(100, 999)}",
                };
            }
            if (plan.Requires(PlanFieldGroups.ServiceConfiguration))
            {
                clinic.ServiceConfiguration = new ServiceConfiguration
                {
                    WorkplaceCount = random.Next(2, 9),
                    OnboardingPreference = random.Next(2) == 0 ? OnboardingPreference.Online : OnboardingPreference.OnSite,
                    TechnicalContactEmail = $"it.{slug}@{EmailDomain}",
                };
            }
            db.Clinics.Add(clinic);
            db.Record(ActivityEventType.ClinicRegistered, createdAt, clinic.Id, detail: planCode);

            if (random.Next(100) < 88)
            {
                var verifiedAt = Past(createdAt.AddMinutes(random.Next(2, 90)), createdAt);
                clinic.EmailVerifiedAt = verifiedAt;
                db.Record(ActivityEventType.ClinicEmailVerified, verifiedAt, clinic.Id);
                db.Record(ActivityEventType.ClinicSignedIn, verifiedAt, clinic.Id);
                // Some clinics came back later (a few of them today, for the "signed in today" metric).
                if (random.Next(3) == 0)
                {
                    db.Record(ActivityEventType.ClinicSignedIn, Past(now.AddHours(-random.Next(0, 30)), verifiedAt), clinic.Id);
                }
            }

            // Older registrations are mostly decided; the newest ones wait for review.
            if (ageDays > 2.5)
            {
                var roll = random.Next(100);
                var decision = roll < 68 ? ClinicStatus.Approved : roll < 84 ? ClinicStatus.Rejected : (ClinicStatus?)null;
                if (decision is { } status)
                {
                    var decidedAt = Past(createdAt.AddHours(random.Next(3, 70)), createdAt);
                    var message = status == ClinicStatus.Rejected
                        ? RejectionReasons[random.Next(RejectionReasons.Length)]
                        : random.Next(3) == 0 ? ApprovalMessages[random.Next(ApprovalMessages.Length)] : null;
                    db.ClinicStatusChanges.Add(new ClinicStatusChange
                    {
                        Id = Guid.CreateVersion7(), ClinicId = clinic.Id, FromStatus = ClinicStatus.Pending, ToStatus = status,
                        AdministratorId = admin.Id, ChangedAt = decidedAt, Reason = message,
                    });
                    clinic.Status = status;
                    clinic.StatusMessage = message;
                    clinic.StatusChangedAt = decidedAt;
                    clinic.UpdatedAt = decidedAt;
                    db.Record(ActivityEventType.ClinicStatusChanged, decidedAt, clinic.Id, admin.Id, $"Pending→{status}");
                }
            }

            if (random.Next(4) == 0)
            {
                var noteAt = Past(createdAt.AddHours(random.Next(1, 30)), createdAt);
                db.AdminNotes.Add(new AdminNote
                {
                    Id = Guid.CreateVersion7(), ClinicId = clinic.Id, AdministratorId = admin.Id,
                    Text = Notes[random.Next(Notes.Length)], CreatedAt = noteAt,
                });
                db.Record(ActivityEventType.AdminNoteAdded, noteAt, clinic.Id, admin.Id);
            }

            if (i % 4 == 1 && clinic.EmailVerifiedAt is { } verified)
            {
                var (question, answer) = Conversations[(i / 4) % Conversations.Length];
                var askedAt = Past(verified.AddHours(random.Next(1, 20)), verified);
                var answered = answer is not null;
                db.ClinicMessages.Add(new ClinicMessage
                {
                    Id = Guid.CreateVersion7(), ClinicId = clinic.Id, Sender = MessageSender.Clinic, Body = question,
                    CreatedAt = askedAt, ReadAt = answered ? askedAt.AddMinutes(30) : null,
                });
                db.Record(ActivityEventType.ClinicMessageSent, askedAt, clinic.Id);
                if (answered)
                {
                    var answeredAt = Past(askedAt.AddHours(2), askedAt);
                    db.ClinicMessages.Add(new ClinicMessage
                    {
                        Id = Guid.CreateVersion7(), ClinicId = clinic.Id, Sender = MessageSender.Administrator, AdministratorId = admin.Id,
                        Body = answer!, CreatedAt = answeredAt, ReadAt = random.Next(2) == 0 ? answeredAt.AddHours(1) : null,
                    });
                    db.Record(ActivityEventType.AdminMessageSent, answeredAt, clinic.Id, admin.Id);
                }
            }
        }

        db.Record(ActivityEventType.DemoDataSeeded, now, administratorId: null, detail: $"clinics={ClinicCount}");
        await db.SaveChangesAsync(ct);
        return ClinicCount;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>Random IČO with a valid modulo-11 check digit (see <see cref="Rules.IsValidIco"/>).</summary>
    private static string RandomIco(Random random)
    {
        var digits = new int[7];
        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            digits[i] = i == 0 ? random.Next(1, 10) : random.Next(10);
            sum += digits[i] * (8 - i);
        }
        var check = (sum % 11) switch { 0 => 1, 1 => 0, var r => 11 - r };
        return string.Concat(digits) + check;
    }

    private static string Slug(string value)
    {
        var normalized = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-');
        var slug = string.Join('-', new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }
}
