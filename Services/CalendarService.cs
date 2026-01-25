using System.Text;

namespace ReelDiscovery.Services;

public class CalendarService
{
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
        var uid = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//ReelDiscovery//Email Generator//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:REQUEST");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{FormatDateTime(now)}");
        sb.AppendLine($"DTSTART:{FormatDateTime(startTime)}");
        sb.AppendLine($"DTEND:{FormatDateTime(endTime)}");
        sb.AppendLine($"SUMMARY:{EscapeIcsText(title)}");
        sb.AppendLine($"DESCRIPTION:{EscapeIcsText(description)}");
        sb.AppendLine($"LOCATION:{EscapeIcsText(location)}");
        sb.AppendLine($"ORGANIZER;CN={EscapeIcsText(organizerName)}:mailto:{organizerEmail}");

        foreach (var (name, email) in attendees)
        {
            sb.AppendLine($"ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;CN={EscapeIcsText(name)}:mailto:{email}");
        }

        sb.AppendLine("STATUS:CONFIRMED");
        sb.AppendLine("SEQUENCE:0");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

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
}
