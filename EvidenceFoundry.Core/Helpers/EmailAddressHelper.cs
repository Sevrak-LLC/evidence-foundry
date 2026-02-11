using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace EvidenceFoundry.Helpers;

public static partial class EmailAddressHelper
{
    private const int MaxLocalPartLength = 64;

    [GeneratedRegex("\\.{2,}")]
    private static partial Regex MultipleDotsRegex();

    public static string GenerateEmail(string firstName, string lastName, string domain, string? preferredEmail = null)
    {
        if (TryGenerateEmail(firstName, lastName, domain, out var email, preferredEmail))
            return email;

        throw new InvalidOperationException("Unable to generate an email address from the provided inputs.");
    }

    public static bool TryGenerateEmail(
        string firstName,
        string lastName,
        string domain,
        out string email,
        string? preferredEmail = null)
    {
        return TryGenerateUniqueEmail(firstName, lastName, domain, null, out email, preferredEmail);
    }

    public static string GenerateUniqueEmail(
        string firstName,
        string lastName,
        string domain,
        ISet<string>? usedEmails,
        string? preferredEmail = null)
    {
        if (TryGenerateUniqueEmail(firstName, lastName, domain, usedEmails, out var email, preferredEmail))
            return email;

        throw new InvalidOperationException("Unable to generate a unique email address from the provided inputs.");
    }

    public static bool TryGenerateUniqueEmail(
        string firstName,
        string lastName,
        string domain,
        ISet<string>? usedEmails,
        out string email,
        string? preferredEmail = null)
    {
        email = string.Empty;
        if (!TryNormalizeDomain(domain, out var cleanedDomain))
            return false;

        var baseLocal = TryExtractLocalPart(preferredEmail, out var preferredLocal)
            ? preferredLocal
            : TryNormalizeLocalPart($"{firstName}.{lastName}", out var normalizedLocal)
                ? normalizedLocal
                : "user";

        baseLocal = TruncateLocalPart(baseLocal);

        var candidate = $"{baseLocal}@{cleanedDomain}";
        if (IsValidEmail(candidate) && (usedEmails == null || !usedEmails.Contains(candidate)))
        {
            email = candidate;
            return true;
        }

        for (var counter = 2; counter < 1000; counter++)
        {
            var local = TruncateLocalPart($"{baseLocal}{counter}");
            candidate = $"{local}@{cleanedDomain}";
            if (IsValidEmail(candidate) && (usedEmails == null || !usedEmails.Contains(candidate)))
            {
                email = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeDomain(string? domain, out string normalized)
    {
        normalized = string.Empty;
        var trimmed = (domain ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains('@'))
            return false;

        var cleaned = string.Concat(trimmed.Where(ch =>
            char.IsLetterOrDigit(ch) || ch == '.' || ch == '-')).Trim('.');
        if (string.IsNullOrWhiteSpace(cleaned) || !cleaned.Contains('.'))
            return false;

        normalized = cleaned;
        return true;
    }

    private static bool TryExtractLocalPart(string? preferredEmail, out string localPart)
    {
        localPart = string.Empty;
        if (string.IsNullOrWhiteSpace(preferredEmail))
            return false;

        var trimmed = preferredEmail.Trim();
        var at = trimmed.IndexOf('@');
        var local = at > 0 ? trimmed[..at] : trimmed;
        return TryNormalizeLocalPart(local, out localPart);
    }

    private static bool TryNormalizeLocalPart(string? value, out string localPart)
    {
        localPart = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

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
        cleaned = MultipleDotsRegex().Replace(cleaned, ".");
        cleaned = cleaned.Trim('.');
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        localPart = cleaned;
        return true;
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
