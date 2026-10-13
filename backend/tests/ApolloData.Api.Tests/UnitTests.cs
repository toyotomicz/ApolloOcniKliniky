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
            "rehor@example.cz", "777123456", "Čáslavská 12", "Ústí nad Labem", "40001", null, null, true);

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
        Assert.Single(PlanCatalog.All, p => p.Highlighted);
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
}
