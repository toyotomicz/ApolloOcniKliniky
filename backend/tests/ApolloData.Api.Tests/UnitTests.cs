using ApolloData.Api.Admin;
using ApolloData.Api.Auth;
using ApolloData.Api.Clinics;
using ApolloData.Api.Common;
using ApolloData.Api.Plans;

namespace ApolloData.Api.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData("25596641", true)]
    [InlineData("00000019", true)]
    [InlineData("25596642", false)]
    [InlineData("1234567", false)]
    [InlineData("abcdefgh", false)]
    public void Ico_checksum_is_validated(string value, bool expected) => Assert.Equal(expected, Rules.IsValidIco(value));

    [Theory]
    [InlineData("jana@example.cz", true)]
    [InlineData("jana.novakova+test@klinika.example.cz", true)]
    [InlineData("jana@", false)]
    [InlineData("jana example@x.cz", false)]
    [InlineData("@example.cz", false)]
    public void Email_format_is_validated(string value, bool expected) => Assert.Equal(expected, Rules.IsValidEmail(value));

    [Fact]
    public void Postal_code_and_phone_are_normalized()
    {
        var errors = new ValidationErrors();
        Assert.Equal("301 00", Rules.PostalCode(errors, "postalCode", "30100"));
        Assert.Equal("301 00", Rules.PostalCode(errors, "postalCode", " 301 00 "));
        Assert.Equal("+420777123456", Rules.Phone(errors, "phone", "+420 777-123-456"));
        Assert.True(errors.IsValid);
    }

    [Fact]
    public void Czech_diacritics_are_preserved()
    {
        var request = new RegistrationRequest("basic", "Poliklinika Žluťoučký kůň", null, "Řehoř", "Šťastný",
            "rehor@example.cz", "777123456", "Čáslavská 12", "Ústí nad Labem", "40001", null, null, true,
            AuthorityConfirmed: true, PublicPhones: ["777123456"]);

        var (result, errors) = ClinicValidator.ValidateRegistration(request);

        Assert.True(errors.IsValid);
        Assert.Equal("Poliklinika Žluťoučký kůň", result!.Name);
        Assert.Equal("Ústí nad Labem", result.Contact.City);
    }

    [Fact]
    public void Plan_catalog_defines_increasing_requirements()
    {
        Assert.Empty(PlanCatalog.Find("basic")!.FieldGroups);
        Assert.True(PlanCatalog.Find("STANDARD")!.Requires(PlanFieldGroups.OperationalContact));
        Assert.False(PlanCatalog.Find("standard")!.Requires(PlanFieldGroups.ServiceConfiguration));
        Assert.True(PlanCatalog.Find("premium")!.Requires(PlanFieldGroups.ServiceConfiguration));
        Assert.Null(PlanCatalog.Find("enterprise"));
        Assert.Equal(["Úroveň 1", "Úroveň 2", "Úroveň 3"], PlanCatalog.All.Select(p => p.Name));
        // "Not part of this level" rows are kept in order, e.g. level 2 has no direct booking but does set capacity.
        Assert.Equal([true, true, false, true, false], PlanCatalog.Find("standard")!.Features.Select(f => f.Included));
        Assert.All(PlanCatalog.Find("premium")!.Features, f => Assert.True(f.Included));
    }

    [Fact]
    public void Otp_codes_are_six_digits_and_hash_is_bound_to_challenge()
    {
        var hasher = new OtpCodeHasher(new byte[32]);
        var codes = Enumerable.Range(0, 200).Select(_ => OtpCodeHasher.GenerateCode()).ToList();
        Assert.All(codes, c => Assert.Matches(@"^\d{6}$", c));
        Assert.True(codes.Distinct().Count() > 150);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var hash = hasher.Hash(a, "123456");
        Assert.True(hasher.Verify(a, "123456", hash));
        Assert.False(hasher.Verify(a, "123457", hash));
        Assert.False(hasher.Verify(b, "123456", hash));
    }

    [Fact]
    public void Like_wildcards_are_escaped()
    {
        Assert.Equal(@"100\% \_ \\", AdminEndpoints.EscapeLike(@"100% _ \"));
    }

    [Theory]
    [InlineData("Oční klinika", "\"Oční klinika\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+420 777", "\"'+420 777\"")]
    [InlineData("-5", "\"'-5\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("Řekl \"ahoj\"", "\"Řekl \"\"ahoj\"\"\"")]
    [InlineData("", "")]
    public void Csv_cells_are_quoted_and_formulas_neutralised(string value, string expected) =>
        Assert.Equal(expected, AuditEndpoints.Cell(value));

    [Fact]
    public void Recovery_codes_are_unique_readable_and_hash_case_insensitively()
    {
        var codes = TwoFactorService.CreateRecoveryCodes();
        Assert.Equal(TwoFactorService.RecoveryCodeCount, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-HJKMNP-Z2-9]{5}-[A-HJKMNP-Z2-9]{5}$", c));
        Assert.Equal(TwoFactorService.HashRecoveryCode(codes[0]), TwoFactorService.HashRecoveryCode(codes[0].ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void Totp_accepts_adjacent_steps_and_never_the_same_step_twice()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero));
        var provider = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var service = new TwoFactorService(provider, time, new AppInfo(config));
        var secret = service.CreateSecret();
        Assert.Equal(secret.Base32, service.Unprotect(secret.Protected));

        var totp = new OtpNet.Totp(OtpNet.Base32Encoding.ToBytes(secret.Base32));
        var code = totp.ComputeTotp(time.GetUtcNow().UtcDateTime);
        var step = service.VerifyCode(secret.Base32, code, lastUsedStep: null);
        Assert.NotNull(step);
        Assert.Null(service.VerifyCode(secret.Base32, code, step));          // replay
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(service.VerifyCode(secret.Base32, code, step));          // previous step, already used
        Assert.NotNull(service.VerifyCode(secret.Base32, code, lastUsedStep: null)); // clock drift of one step is tolerated
        time.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(service.VerifyCode(secret.Base32, code, lastUsedStep: null));    // too old
        Assert.Null(service.VerifyCode(secret.Base32, "12345", null));
        Assert.Null(service.VerifyCode(secret.Base32, "abcdef", null));
    }

    [Fact]
    public void Totp_secret_is_grouped_for_manual_entry() =>
        Assert.Equal("ABCD EFGH IJ", TwoFactorService.FormatForDisplay("ABCDEFGHIJ"));

    [Theory]
    [InlineData("+420777123456", "+420 777 123 456")]
    [InlineData("777123456", "777 123 456")]
    [InlineData("+4915112345678", "+4915112345678")]
    public void Pdf_phone_numbers_are_grouped(string stored, string expected) =>
        Assert.Equal(expected, ApolloData.Api.Documents.ConfirmationPdf.FormatPhone(stored));

    [Fact]
    public void Csv_status_details_are_readable()
    {
        Assert.Equal("Čeká na posouzení → Schváleno (hromadně)",
            AuditEndpoints.DescribeDetail(ApolloData.Api.Domain.ActivityEventType.ClinicStatusChanged, "Pending→Approved;bulk"));
        Assert.Equal("rows=3", AuditEndpoints.DescribeDetail(ApolloData.Api.Domain.ActivityEventType.AuditLogExported, "rows=3"));
    }
}
