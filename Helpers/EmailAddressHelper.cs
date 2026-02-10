using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace EvidenceFoundry.Helpers;

public static class EmailAddressHelper
{
    private const int MaxLocalPartLength = 64;
    private static readonly Regex MultipleDotsRegex = new("\\.{2,}", RegexOptions.Compiled);

    public static string GenerateEmail(string firstName, string lastName, string domain, string? preferredEmail = null)
    {
        return GenerateUniqueEmail(firstName, lastName, domain, null, preferredEmail);
    }

    public static string GenerateUniqueEmail(
        string firstName,
        string lastName,
        string domain,
        ISet<string>? usedEmails,
        string? preferredEmail = null)
    {
        var cleanedDomain = NormalizeDomain(domain);
        if (string.IsNullOrWhiteSpace(cleanedDomain))
            return string.Empty;

        var preferredLocal = ExtractLocalPart(preferredEmail);
        var baseLocal = !string.IsNullOrWhiteSpace(preferredLocal)
            ? preferredLocal
            : NormalizeLocalPart($"{firstName}.{lastName}");

        if (string.IsNullOrWhiteSpace(baseLocal))
            baseLocal = "user";

        baseLocal = TruncateLocalPart(baseLocal);

        var candidate = $"{baseLocal}@{cleanedDomain}";
        if (IsValidEmail(candidate) && (usedEmails == null || !usedEmails.Contains(candidate)))
            return candidate;

        for (var counter = 2; counter < 1000; counter++)
        {
            var local = TruncateLocalPart($"{baseLocal}{counter}");
            candidate = $"{local}@{cleanedDomain}";
            if (IsValidEmail(candidate) && (usedEmails == null || !usedEmails.Contains(candidate)))
                return candidate;
        }

        return string.Empty;
    }

    private static string NormalizeDomain(string domain)
    {
        var trimmed = (domain ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains('@'))
            return string.Empty;

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '-')
                sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim('.');
        if (string.IsNullOrWhiteSpace(cleaned) || !cleaned.Contains('.'))
            return string.Empty;

        return cleaned;
    }

    private static string ExtractLocalPart(string? preferredEmail)
    {
        if (string.IsNullOrWhiteSpace(preferredEmail))
            return string.Empty;

        var trimmed = preferredEmail.Trim();
        var at = trimmed.IndexOf('@');
        var local = at > 0 ? trimmed[..at] : trimmed;
        return NormalizeLocalPart(local);
    }

    private static string NormalizeLocalPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            var lower = char.ToLowerInvariant(ch);
            if ((lower >= 'a' && lower <= 'z') ||
                (lower >= '0' && lower <= '9') ||
                lower == '.' || lower == '_' || lower == '-')
            {
                sb.Append(lower);
            }
        }

        var cleaned = sb.ToString();
        cleaned = MultipleDotsRegex.Replace(cleaned, ".");
        cleaned = cleaned.Trim('.');

        return cleaned;
    }

    private static string TruncateLocalPart(string local)
    {
        return local.Length <= MaxLocalPartLength ? local : local[..MaxLocalPartLength];
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new MailAddress(email);
            return address.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
