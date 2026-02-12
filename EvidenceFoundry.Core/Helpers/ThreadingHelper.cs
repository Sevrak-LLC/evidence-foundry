using System.Globalization;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static class ThreadingHelper
{
    public static string GenerateMessageId(EmailMessage email, string domain)
    {
        ArgumentNullException.ThrowIfNull(email);

        var resolvedDomain = ResolveDomain(domain, email.From?.Domain);
        var token = DeterministicIdHelper.CreateShortToken(
            "message-id",
            32,
            email.Id.ToString("N"),
            email.SequenceInThread.ToString(CultureInfo.InvariantCulture),
            email.SentDate.ToString("O"),
            email.Subject,
            email.From?.Email ?? string.Empty);

        return $"<{token}@{resolvedDomain}>";
    }

    public static void SetupThreading(EmailThread thread, string domain)
    {
        var emailLookup = thread.EmailMessages.ToDictionary(m => m.Id);
        string? previousMessageId = null;

        foreach (var email in thread.EmailMessages
                     .OrderBy(m => m.SentDate)
                     .ThenBy(m => m.SequenceInThread)
                     .ThenBy(m => m.Id))
        {
            email.MessageId = GenerateMessageId(email, domain);

            if (email.ParentEmailId is { } parentId && emailLookup.TryGetValue(parentId, out var parent))
            {
                email.InReplyTo = parent.MessageId;
                var refs = new List<string>();
                if (parent.References.Count > 0)
                    refs.AddRange(parent.References);
                refs.Add(parent.MessageId);
                email.SetReferences(refs);
            }
            else if (previousMessageId != null)
            {
                email.InReplyTo = previousMessageId;
                email.SetReferences(new List<string> { previousMessageId });
            }

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
        var lines = TextSplitHelper.SplitLines(text, StringSplitOptions.None);
        return string.Join("\n", lines.Select(line => $"> {line}"));
    }

    private static string ResolveDomain(string? domain, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return domain.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();
        return "generated.local";
    }
}
