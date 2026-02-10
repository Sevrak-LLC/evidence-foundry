using System.Net.Mail;
using MimeKit;
using EvidenceFoundry.Models;
using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Services;

public class EmlFileService
{
    private const int MaxFileNameLength = 180;

    public async Task SaveEmailAsEmlAsync(
        EmailMessage email,
        string outputFolder,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();

        // Set Message-ID (required for threading)
        // If MessageId is empty, generate a new one
        var messageId = SanitizeHeaderValue(email.MessageId, 200);
        if (string.IsNullOrEmpty(messageId))
        {
            messageId = $"<{Guid.NewGuid():N}.{DateTime.UtcNow.Ticks}@generated.local>";
        }
        message.MessageId = SanitizeHeaderValue(messageId, 200).Trim('<', '>');

        // Set date
        message.Date = new DateTimeOffset(email.SentDate);

        // Set subject
        message.Subject = SanitizeHeaderValue(email.Subject, 255);

        // From
        message.From.Add(CreateFromAddress(email.From));

        // To
        foreach (var to in email.To)
        {
            var address = TryCreateMailboxAddress(to.FullName, to.Email);
            if (address != null)
            {
                message.To.Add(address);
            }
        }

        // Cc
        foreach (var cc in email.Cc)
        {
            var address = TryCreateMailboxAddress(cc.FullName, cc.Email);
            if (address != null)
            {
                message.Cc.Add(address);
            }
        }

        // Threading headers
        var inReplyTo = SanitizeHeaderValue(email.InReplyTo, 200);
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo.Trim('<', '>');
        }

        if (email.References.Count > 0)
        {
            foreach (var reference in email.References)
            {
                var sanitizedReference = SanitizeHeaderValue(reference, 200).Trim('<', '>');
                if (!string.IsNullOrEmpty(sanitizedReference))
                {
                    message.References.Add(sanitizedReference);
                }
            }
        }

        // Build body - always use HTML for consistent rendering across email clients
        var builder = new BodyBuilder();

        if (!string.IsNullOrEmpty(email.BodyHtml))
        {
            builder.HtmlBody = email.BodyHtml;
        }
        else
        {
            // Fallback only if HTML generation somehow failed
            builder.HtmlBody = HtmlEmailFormatter.ConvertToHtml(email.BodyPlain);
        }

        // Add attachments
        foreach (var attachment in email.Attachments)
        {
            if (attachment.Content != null)
            {
                // Create the MIME part explicitly to ensure proper content type
                var mimeType = ContentType.Parse(attachment.MimeType);
                var mimePart = new MimePart(mimeType)
                {
                    Content = new MimeContent(new MemoryStream(attachment.Content)),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = attachment.FileName
                };

                // Handle inline images vs regular attachments
                if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
                {
                    mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
                    mimePart.ContentId = attachment.ContentId;
                    // Inline images go in LinkedResources so they can be referenced by cid:
                    builder.LinkedResources.Add(mimePart);
                }
                else
                {
                    mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);
                    builder.Attachments.Add(mimePart);
                }
            }
        }

        message.Body = builder.ToMessageBody();

        // Generate filename and save
        var fileName = GetSafeEmlFileName(email.GeneratedFileName, email);
        email.GeneratedFileName = fileName;

        var filePath = Path.Combine(outputFolder, fileName);

        await using var stream = File.Create(filePath);
        await message.WriteToAsync(stream, ct);
    }

    public async Task SaveAllEmailsAsync(
        List<EmailThread> threads,
        string outputFolder,
        bool organizeBySender = false,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        CancellationToken ct = default,
        int? maxDegreeOfParallelism = null,
        bool releaseAttachmentContent = false)
    {
        // Ensure output folder exists
        Directory.CreateDirectory(outputFolder);

        var allEmails = threads.SelectMany(t => t.EmailMessages).ToList();
        var total = allEmails.Count;
        var completed = 0;

        var degree = Math.Max(1, maxDegreeOfParallelism ?? 1);

        if (degree == 1)
        {
            foreach (var email in allEmails)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = FileNameHelper.GenerateEmlFileName(email);
                email.GeneratedFileName = fileName;

                // Determine the target folder
                var targetFolder = outputFolder;
                if (organizeBySender && email.From != null && !string.IsNullOrEmpty(email.From.Email))
                {
                    // Create subfolder based on sender email address
                    var senderFolder = SanitizeFolderName(email.From.Email);
                    targetFolder = Path.Combine(outputFolder, senderFolder);
                    Directory.CreateDirectory(targetFolder);
                }

                await SaveEmailAsEmlAsync(email, targetFolder, ct);

                if (releaseAttachmentContent && email.Attachments.Count > 0)
                {
                    foreach (var attachment in email.Attachments)
                    {
                        attachment.Content = null;
                    }
                }

                completed++;
                progress?.Report((completed, total, fileName));
            }
        }
        else
        {
            await Parallel.ForEachAsync(allEmails, new ParallelOptions
            {
                MaxDegreeOfParallelism = degree,
                CancellationToken = ct
            }, async (email, token) =>
            {
                var fileName = FileNameHelper.GenerateEmlFileName(email);
                email.GeneratedFileName = fileName;

                // Determine the target folder
                var targetFolder = outputFolder;
                if (organizeBySender && email.From != null && !string.IsNullOrEmpty(email.From.Email))
                {
                    var senderFolder = SanitizeFolderName(email.From.Email);
                    targetFolder = Path.Combine(outputFolder, senderFolder);
                    Directory.CreateDirectory(targetFolder);
                }

                await SaveEmailAsEmlAsync(email, targetFolder, token);

                if (releaseAttachmentContent && email.Attachments.Count > 0)
                {
                    foreach (var attachment in email.Attachments)
                    {
                        attachment.Content = null;
                    }
                }

                var newCompleted = Interlocked.Increment(ref completed);
                progress?.Report((newCompleted, total, fileName));
            });
        }
    }

    public Task SaveThreadEmailsAsync(
        EmailThread thread,
        string outputFolder,
        bool organizeBySender = false,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        CancellationToken ct = default,
        bool releaseAttachmentContent = false)
    {
        return SaveAllEmailsAsync(
            new List<EmailThread> { thread },
            outputFolder,
            organizeBySender,
            progress,
            ct,
            maxDegreeOfParallelism: 1,
            releaseAttachmentContent: releaseAttachmentContent);
    }

    private static string SanitizeFolderName(string email)
    {
        // Remove or replace invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = email;
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized;
    }

    private static MailboxAddress CreateFromAddress(Character from)
    {
        var displayName = SanitizeHeaderText(from.FullName, 128);
        if (TryNormalizeEmail(from.Email, out var normalized))
        {
            return new MailboxAddress(displayName, normalized);
        }

        var fallbackName = string.IsNullOrWhiteSpace(displayName) ? "Unknown Sender" : displayName;
        return new MailboxAddress(fallbackName, "unknown@invalid.local");
    }

    private static MailboxAddress? TryCreateMailboxAddress(string? displayName, string? email)
    {
        if (!TryNormalizeEmail(email, out var normalized))
        {
            return null;
        }

        var safeName = SanitizeHeaderText(displayName, 128);
        return new MailboxAddress(safeName, normalized);
    }

    private static string SanitizeHeaderText(string? value, int maxLength = 256)
    {
        var trimmed = (value ?? string.Empty).Trim();
        trimmed = trimmed.Replace("\r", "").Replace("\n", "");
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string SanitizeHeaderValue(string? value, int maxLength = 998)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Replace("\r", "").Replace("\n", "").Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static bool TryNormalizeEmail(string? value, out string normalized)
    {
        normalized = string.Empty;
        var candidate = SanitizeHeaderText(value, 320);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

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

    private static string GetSafeEmlFileName(string? requested, EmailMessage email)
    {
        var fallback = FileNameHelper.GenerateEmlFileName(email);
        var candidate = string.IsNullOrWhiteSpace(requested) ? fallback : requested;
        candidate = Path.GetFileName(candidate.Trim());
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = fallback;
        }

        var extension = Path.GetExtension(candidate);
        if (!string.Equals(extension, ".eml", StringComparison.OrdinalIgnoreCase))
        {
            extension = ".eml";
        }

        var baseName = Path.GetFileNameWithoutExtension(candidate);
        baseName = FileNameHelper.SanitizeForFileName(baseName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = Path.GetFileNameWithoutExtension(fallback);
        }

        var maxBaseLength = Math.Max(1, MaxFileNameLength - extension.Length);
        if (baseName.Length > maxBaseLength)
        {
            baseName = baseName[..maxBaseLength];
        }

        return $"{baseName}{extension}";
    }
}
