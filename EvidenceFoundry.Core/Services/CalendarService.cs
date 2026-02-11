using System.Net.Mail;
using System.Text;
using System.Linq;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Services;

public class CalendarService
{
    private const int MaxIcsLineLength = 75;

    public sealed record CalendarInviteRequest(
        string Title,
        string Description,
        DateTime StartTime,
        DateTime EndTime,
        string Location,
        string OrganizerName,
        string OrganizerEmail,
        IReadOnlyList<(string name, string email)> Attendees);

    /// <summary>
    /// Creates an ICS calendar invite file
    /// </summary>
    public static byte[] CreateCalendarInvite(CalendarInviteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startTime = request.StartTime;
        var endTime = request.EndTime;
        if (endTime <= startTime)
        {
            endTime = startTime.AddMinutes(60);
        }

        var organizerEmailValue = TryNormalizeEmail(request.OrganizerEmail, out var normalizedOrganizer)
            ? normalizedOrganizer
            : "unknown@invalid.local";
        var organizerNameValue = EscapeIcsText(SanitizeHeaderText(request.OrganizerName));
        var attendees = request.Attendees ?? Array.Empty<(string name, string email)>();
        var uid = BuildCalendarUid(request, organizerEmailValue, attendees);
        var now = DateTime.UtcNow;

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
        AppendFoldedLine(sb, $"SUMMARY:{EscapeIcsText(request.Title)}");
        AppendFoldedLine(sb, $"DESCRIPTION:{EscapeIcsText(request.Description)}");
        AppendFoldedLine(sb, $"LOCATION:{EscapeIcsText(request.Location)}");
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

    private static string BuildCalendarUid(
        CalendarInviteRequest request,
        string organizerEmail,
        IReadOnlyList<(string name, string email)> attendees)
    {
        var attendeeSeed = string.Join(
            ";",
            attendees.Select(a => a.email)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase));

        var token = DeterministicIdHelper.CreateShortToken(
            "calendar-uid",
            32,
            request.Title,
            request.Description,
            request.Location,
            request.StartTime.ToString("O"),
            request.EndTime.ToString("O"),
            organizerEmail,
            attendeeSeed);

        var domain = ExtractEmailDomain(organizerEmail);
        return $"{token}@{domain}";
    }

    private static string ExtractEmailDomain(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex >= 0 && atIndex < email.Length - 1)
            return email[(atIndex + 1)..];

        return "generated.local";
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
