using System.Net.Mail;
using System.Text.RegularExpressions;

namespace ApolloData.Api.Common;

/// <summary>Collects field errors keyed by the camelCase JSON path of the field (e.g. <c>operationalContact.email</c>).</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }
        list.Add(message);
    }

    public bool Has(string field) => _errors.ContainsKey(field);

    public IDictionary<string, string[]> ToDictionary() => _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}

/// <summary>Reusable field rules with Czech messages. Each method returns the normalized value or null.</summary>
public static partial class Rules
{
    public static string? Required(ValidationErrors errors, string field, string? value, int maxLength, int minLength = 1)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            errors.Add(field, "Toto pole je povinné.");
            return null;
        }
        return Length(errors, field, trimmed, maxLength, minLength);
    }

    public static string? Optional(ValidationErrors errors, string field, string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : Length(errors, field, trimmed, maxLength, 1);
    }

    private static string? Length(ValidationErrors errors, string field, string value, int maxLength, int minLength)
    {
        if (value.Length < minLength)
        {
            errors.Add(field, $"Zadejte alespoň {minLength} znaky.");
            return null;
        }
        if (value.Length > maxLength)
        {
            errors.Add(field, $"Maximální délka je {maxLength} znaků.");
            return null;
        }
        return value;
    }

    public static string? Email(ValidationErrors errors, string field, string? value, bool required = true)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required) errors.Add(field, "Zadejte e-mailovou adresu.");
            return null;
        }
        if (!IsValidEmail(trimmed))
        {
            errors.Add(field, "Zadejte platnou e-mailovou adresu.");
            return null;
        }
        return trimmed;
    }

    public static bool IsValidEmail(string value) =>
        value.Length <= 254
        && EmailPattern().IsMatch(value)
        && MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Accepts Czech and international numbers, e.g. "+420 777 123 456" or "777123456". Stored without spaces.</summary>
    public static string? Phone(ValidationErrors errors, string field, string? value, bool required = true)
    {
        var compact = value is null ? null : WhitespaceOrDashes().Replace(value, "");
        if (string.IsNullOrEmpty(compact))
        {
            if (required) errors.Add(field, "Zadejte telefonní číslo.");
            return null;
        }
        if (!PhonePattern().IsMatch(compact))
        {
            errors.Add(field, "Zadejte platné telefonní číslo, např. +420 777 123 456.");
            return null;
        }
        return compact;
    }

    /// <summary>Optional public website. "www.klinika.cz" is accepted and stored as "https://www.klinika.cz".</summary>
    public static string? Website(ValidationErrors errors, string field, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        var candidate = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : $"https://{trimmed}";
        if (candidate.Length > 300
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !uri.Host.Contains('.')
            || uri.Host.EndsWith('.'))
        {
            errors.Add(field, "Zadejte platnou adresu webu, např. www.klinika.cz.");
            return null;
        }
        return candidate;
    }

    /// <summary>Czech postal code (PSČ), normalized to "123 45".</summary>
    public static string? PostalCode(ValidationErrors errors, string field, string? value)
    {
        var compact = value is null ? null : WhitespaceOrDashes().Replace(value, "");
        if (string.IsNullOrEmpty(compact))
        {
            errors.Add(field, "Zadejte PSČ.");
            return null;
        }
        if (!PostalCodePattern().IsMatch(compact))
        {
            errors.Add(field, "PSČ musí mít 5 číslic, např. 301 00.");
            return null;
        }
        return $"{compact[..3]} {compact[3..]}";
    }

    /// <summary>Optional Czech company identifier (IČO): 8 digits with a modulo-11 check digit.</summary>
    public static string? OrganizationId(ValidationErrors errors, string field, string? value)
    {
        var compact = value is null ? null : WhitespaceOrDashes().Replace(value, "");
        if (string.IsNullOrEmpty(compact)) return null;
        if (!IsValidIco(compact))
        {
            errors.Add(field, "IČO musí mít 8 číslic a platný kontrolní součet.");
            return null;
        }
        return compact;
    }

    public static bool IsValidIco(string value)
    {
        if (value.Length != 8 || !value.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            sum += (value[i] - '0') * (8 - i);
        }
        var remainder = sum % 11;
        var check = remainder switch
        {
            0 => 1,
            1 => 0,
            _ => 11 - remainder,
        };
        return value[7] - '0' == check;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\+?[0-9]{9,15}$")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[0-9]{5}$")]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex(@"[\s\-]")]
    private static partial Regex WhitespaceOrDashes();
}
