using System.Globalization;
using System.Linq;
using System.Text;
using EvidenceFoundry.Helpers;
using Serilog;

namespace EvidenceFoundry.Services;

public partial class CalendarService
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
        => CreateCalendarInvite(request, Serilog.Log.Logger.ForContext<CalendarService>());

    public static byte[] CreateCalendarInvite(
        CalendarInviteRequest request,
        ILogger? logger)
    {
        var log = logger ?? Serilog.Log.Logger.ForContext<CalendarService>();

        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Location))
            throw new ArgumentException("Location is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OrganizerName))
            throw new ArgumentException("Organizer name is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OrganizerEmail))
            throw new ArgumentException("Organizer email is required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Attendees);

        var startTime = request.StartTime;
        var endTime = request.EndTime;
        if (endTime <= startTime)
        {
            endTime = startTime.AddMinutes(60);
            Log.CalendarInviteEndTimeAdjusted(log);
        }

        var organizerEmailValue = EmailAddressHelper.TryNormalizeEmail(request.OrganizerEmail, out var normalizedOrganizer)
            ? normalizedOrganizer
            : "unknown@invalid.local";
        if (organizerEmailValue == "unknown@invalid.local")
        {
            Log.OrganizerEmailInvalid(log);
        }

        Log.CreatingCalendarInvite(
            log,
            request.Attendees?.Count ?? 0,
            (int)Math.Round((endTime - startTime).TotalMinutes));

        var organizerNameValue = EscapeIcsText(HeaderValueHelper.SanitizeHeaderText(request.OrganizerName));
        var attendees = request.Attendees ?? Array.Empty<(string name, string email)>();
        var uid = BuildCalendarUid(request, organizerEmailValue, attendees);
        var now = Clock.UtcNowDateTime;

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

        var invalidAttendeeCount = 0;
        foreach (var (name, email) in attendees)
        {
            if (!EmailAddressHelper.TryNormalizeEmail(email, out var normalizedEmail))
            {
                invalidAttendeeCount++;
                continue;
            }

            var attendeeName = EscapeIcsText(HeaderValueHelper.SanitizeHeaderText(name));
            AppendFoldedLine(sb, $"ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;CN={attendeeName}:mailto:{normalizedEmail}");
        }

        if (invalidAttendeeCount > 0)
        {
            Log.SkippedInvalidAttendees(log, invalidAttendeeCount);
        }

        AppendFoldedLine(sb, "STATUS:CONFIRMED");
        AppendFoldedLine(sb, "SEQUENCE:0");
        AppendFoldedLine(sb, "END:VEVENT");
        AppendFoldedLine(sb, "END:VCALENDAR");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static class Log
    {
        public static void CalendarInviteEndTimeAdjusted(ILogger logger)
            => logger.Warning("Calendar invite end time was not after start; defaulted to 60-minute duration.");

        public static void OrganizerEmailInvalid(ILogger logger)
            => logger.Warning("Organizer email was invalid; using fallback address.");

        public static void CreatingCalendarInvite(ILogger logger, int attendeeCount, int durationMinutes)
            => logger.Information(
                "Creating calendar invite with {AttendeeCount} attendees and duration {DurationMinutes} minutes.",
                attendeeCount,
                durationMinutes);

        public static void SkippedInvalidAttendees(ILogger logger, int invalidAttendeeCount)
            => logger.Warning(
                "Skipped {InvalidAttendeeCount} attendee(s) due to invalid email addresses.",
                invalidAttendeeCount);
    }

    private static string FormatDateTime(DateTime dt)
    {
        var normalized = Clock.EnsureKind(dt, DateTimeKind.Local);
        var utc = normalized.Kind == DateTimeKind.Utc ? normalized : normalized.ToUniversalTime();
        return utc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
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
