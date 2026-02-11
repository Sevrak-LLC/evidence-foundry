using System.Net.Mail;
using System.Text;

namespace EvidenceFoundry.Services;

public class CalendarService
{
    private const int MaxIcsLineLength = 75;

    /// <summary>
    /// Creates an ICS calendar invite file
    /// </summary>
    public byte[] CreateCalendarInvite(
        string title,
        string description,
        DateTime startTime,
        DateTime endTime,
        string location,
        string organizerName,
        string organizerEmail,
        List<(string name, string email)> attendees)
    {
        if (endTime <= startTime)
        {
            endTime = startTime.AddMinutes(60);
        }

        var uid = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var organizerEmailValue = TryNormalizeEmail(organizerEmail, out var normalizedOrganizer)
            ? normalizedOrganizer
            : "unknown@invalid.local";
        var organizerNameValue = EscapeIcsText(SanitizeHeaderText(organizerName));

        var sb = new StringBuilder();
        AppendFoldedLine(sb, "BEGIN:VCALENDAR");
        AppendFoldedLine(sb, "VERSION:2.0");
        AppendFoldedLine(sb, "PRODID:-//EvidenceFoundry//Email Generator//EN");
        AppendFoldedLine(sb, "CALSCALE:GREGORIAN");
        AppendFoldedLine(sb, "METHOD:REQUEST");
        AppendFoldedLine(sb, "BEGIN:VEVENT");
        AppendFoldedLine(sb, $"UID:{uid}");
        AppendFoldedLine(sb, $"DTSTAMP:{FormatDateTime(now)}");
        AppendFoldedLine(sb, $"DTSTART:{FormatDateTime(startTime)}");
        AppendFoldedLine(sb, $"DTEND:{FormatDateTime(endTime)}");
        AppendFoldedLine(sb, $"SUMMARY:{EscapeIcsText(title)}");
        AppendFoldedLine(sb, $"DESCRIPTION:{EscapeIcsText(description)}");
        AppendFoldedLine(sb, $"LOCATION:{EscapeIcsText(location)}");
        AppendFoldedLine(sb, $"ORGANIZER;CN={organizerNameValue}:mailto:{organizerEmailValue}");

        foreach (var (name, email) in attendees)
        {
            if (!TryNormalizeEmail(email, out var normalizedEmail))
            {
                continue;
            }

            var attendeeName = EscapeIcsText(SanitizeHeaderText(name));
            AppendFoldedLine(sb, $"ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;CN={attendeeName}:mailto:{normalizedEmail}");
        }

        AppendFoldedLine(sb, "STATUS:CONFIRMED");
        AppendFoldedLine(sb, "SEQUENCE:0");
        AppendFoldedLine(sb, "END:VEVENT");
        AppendFoldedLine(sb, "END:VCALENDAR");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string FormatDateTime(DateTime dt)
    {
        return dt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
    }

    private static string EscapeIcsText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // ICS escaping rules
        return text
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    private static string SanitizeHeaderText(string? value, int maxLength = 256)
    {
        var trimmed = (value ?? string.Empty).Trim();
        trimmed = trimmed.Replace("\r", "").Replace("\n", "");
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static bool TryNormalizeEmail(string? value, out string normalized)
    {
        normalized = string.Empty;
        var candidate = SanitizeHeaderText(value, 320);
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        try
        {
            var address = new MailAddress(candidate);
            normalized = address.Address;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendFoldedLine(StringBuilder sb, string line)
    {
        foreach (var folded in FoldLine(line))
        {
            sb.AppendLine(folded);
        }
    }

    private static IEnumerable<string> FoldLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            yield return string.Empty;
            yield break;
        }

        var segment = new StringBuilder();
        var segmentBytes = 0;
        var firstSegment = true;

        foreach (var rune in line.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            var limit = firstSegment ? MaxIcsLineLength : MaxIcsLineLength - 1;

            if (segmentBytes + runeBytes > limit && segment.Length > 0)
            {
                yield return firstSegment ? segment.ToString() : $" {segment}";
                segment.Clear();
                segmentBytes = 0;
                firstSegment = false;
            }

            segment.Append(runeText);
            segmentBytes += runeBytes;
        }

        if (segment.Length > 0)
        {
            yield return firstSegment ? segment.ToString() : $" {segment}";
        }
    }
}
