using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static class ThreadingHelper
{
    private static readonly string[] LineBreakSeparators = { "\r\n", "\n" };

    public static string GenerateMessageId(string domain)
    {
        var uniquePart = $"{Guid.NewGuid():N}.{DateTime.UtcNow.Ticks}";
        return $"<{uniquePart}@{domain}>";
    }

    public static void SetupThreading(EmailThread thread, string domain)
    {
        string? previousMessageId = null;
        var references = new List<string>();

        foreach (var email in thread.EmailMessages.OrderBy(m => m.SequenceInThread))
        {
            // Generate unique Message-ID
            email.MessageId = GenerateMessageId(domain);

            if (previousMessageId != null)
            {
                // This is a reply
                email.InReplyTo = previousMessageId;
                email.References = new List<string>(references);
            }

            // Add current message to references for future replies
            references.Add(email.MessageId);
            previousMessageId = email.MessageId;
        }
    }

    public static string AddReplyPrefix(string subject)
    {
        if (subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }
        return $"RE: {subject}";
    }

    public static string AddForwardPrefix(string subject)
    {
        if (subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase) ||
            subject.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }
        return $"FW: {subject}";
    }

    public static string GetCleanSubject(string subject)
    {
        // Remove RE:, FW:, Fwd: prefixes for comparison
        var cleaned = subject;
        while (true)
        {
            var trimmed = cleaned.TrimStart();
            if (trimmed.StartsWith("RE:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("FW:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[3..];
            else if (trimmed.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase))
                cleaned = trimmed[4..];
            else
                break;
        }
        return cleaned.Trim();
    }

    /// <summary>
    /// Formats quoted content for a reply email
    /// </summary>
    public static string FormatQuotedReply(EmailMessage originalEmail)
    {
        var header = $"On {originalEmail.SentDate:ddd, MMM d, yyyy} at {originalEmail.SentDate:h:mm tt}, {originalEmail.From.FullName} <{originalEmail.From.Email}> wrote:";
        var quotedBody = QuoteText(originalEmail.BodyPlain);
        return $"\n\n{header}\n{quotedBody}";
    }

    /// <summary>
    /// Formats forwarded content for a forward email
    /// </summary>
    public static string FormatForwardedContent(EmailMessage originalEmail)
    {
        var toList = string.Join("; ", originalEmail.To.Select(c => $"{c.FullName} <{c.Email}>"));
        var ccList = originalEmail.Cc.Count > 0
            ? $"\nCc: {string.Join("; ", originalEmail.Cc.Select(c => $"{c.FullName} <{c.Email}>"))}"
            : "";

        var header = $@"

---------- Forwarded message ---------
From: {originalEmail.From.FullName} <{originalEmail.From.Email}>
Date: {originalEmail.SentDate:ddd, MMM d, yyyy} at {originalEmail.SentDate:h:mm tt}
Subject: {originalEmail.Subject}
To: {toList}{ccList}

";
        return header + originalEmail.BodyPlain;
    }

    /// <summary>
    /// Quotes text with > prefix for each line
    /// </summary>
    public static string QuoteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "> ";
        var lines = text.Split(LineBreakSeparators, StringSplitOptions.None);
        return string.Join("\n", lines.Select(line => $"> {line}"));
    }
}
