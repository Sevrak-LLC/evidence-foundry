using System.Text.RegularExpressions;
using ReelDiscovery.Models;

namespace ReelDiscovery.Helpers;

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

        return string.IsNullOrEmpty(sanitized) ? "unnamed" : sanitized;
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
