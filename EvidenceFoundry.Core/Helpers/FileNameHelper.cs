using System.Text.RegularExpressions;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static partial class FileNameHelper
{
    public static string GenerateEmlFileName(EmailMessage email)
    {
        var timestamp = DateHelper.FormatForFileName(email.SentDate);
        var senderName = SanitizeForFileName(email.From.LastName);
        var subjectSnippet = SanitizeForFileName(TruncateSubject(email.Subject, 30));
        var uniqueId = email.Id.ToString("N")[..6];

        return $"{timestamp}_{senderName}_{subjectSnippet}_{uniqueId}.eml";
    }

    public static string GenerateAttachmentFileName(Attachment attachment, EmailMessage email)
    {
        var dateStr = email.SentDate.ToString("yyyyMMdd");
        var subjectPart = SanitizeForFileName(TruncateSubject(
            ThreadingHelper.GetCleanSubject(email.Subject), 25));

        var typeName = attachment.Type switch
        {
            AttachmentType.Word => "Document",
            AttachmentType.Excel => "Spreadsheet",
            AttachmentType.PowerPoint => "Presentation",
            _ => "File"
        };

        return $"{subjectPart}_{typeName}_{dateStr}{attachment.Extension}";
    }

    public static string GenerateImageFileName(EmailMessage email, string? description, string? contentId)
    {
        var token = DeterministicIdHelper.CreateShortToken(
            "image-file",
            6,
            email.Id.ToString("N"),
            description ?? string.Empty,
            contentId ?? string.Empty);

        return $"image_{email.SentDate:yyyyMMdd}_{token}.png";
    }

    public static string GenerateCalendarInviteFileName(
        EmailMessage email,
        DateTime startTime,
        string? title,
        string? organizerEmail)
    {
        var token = DeterministicIdHelper.CreateShortToken(
            "calendar-invite",
            6,
            email.Id.ToString("N"),
            startTime.ToString("O"),
            title ?? string.Empty,
            organizerEmail ?? string.Empty);

        return $"invite_{startTime:yyyyMMdd}_{token}.ics";
    }

    public static string SanitizeForFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "unnamed";

        // Remove invalid filename characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Where(c => !invalid.Contains(c)).ToArray());

        // Replace spaces with underscores
        sanitized = sanitized.Replace(" ", "_");

        // Remove multiple consecutive underscores
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_");

        // Trim underscores from start/end
        sanitized = sanitized.Trim('_');

        // Windows does not allow trailing dots/spaces or reserved device names.
        sanitized = sanitized.Trim(' ', '.');
        if (string.IsNullOrEmpty(sanitized))
            return "unnamed";

        if (IsReservedDeviceName(sanitized))
        {
            sanitized = $"_{sanitized}";
        }

        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized;
    }

    private static bool IsReservedDeviceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var baseName = value;
        var dotIndex = value.IndexOf('.');
        if (dotIndex >= 0)
            baseName = value[..dotIndex];

        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
               || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
               || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
               || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
               || baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && IsDeviceNumber(baseName, "COM")
               || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) && IsDeviceNumber(baseName, "LPT");
    }

    private static bool IsDeviceNumber(string value, string prefix)
    {
        if (value.Length != prefix.Length + 1)
            return false;

        var suffix = value[^1];
        return suffix is >= '1' and <= '9';
    }

    private static string TruncateSubject(string subject, int maxLength)
    {
        // Remove RE:, FW: prefixes for cleaner names
        subject = ThreadingHelper.GetCleanSubject(subject);

        if (string.IsNullOrWhiteSpace(subject))
            return "NoSubject";

        return subject.Length <= maxLength ? subject : subject[..maxLength];
    }

    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();
}
