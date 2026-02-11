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
        var message = CreateMessage(email);

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

                var fileName = await ProcessEmailAsync(
                    email,
                    outputFolder,
                    organizeBySender,
                    releaseAttachmentContent,
                    ct);

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
                var fileName = await ProcessEmailAsync(
                    email,
                    outputFolder,
                    organizeBySender,
                    releaseAttachmentContent,
                    token);

                var newCompleted = Interlocked.Increment(ref completed);
                progress?.Report((newCompleted, total, fileName));
            });
        }
    }

    private static MimeMessage CreateMessage(EmailMessage email)
    {
        var message = new MimeMessage
        {
            MessageId = GetMessageId(email),
            Date = new DateTimeOffset(email.SentDate),
            Subject = SanitizeHeaderValue(email.Subject, 255)
        };

        message.From.Add(CreateFromAddress(email.From));
        AddRecipients(message.To, email.To);
        AddRecipients(message.Cc, email.Cc);
        ApplyThreadingHeaders(message, email);
        message.Body = BuildBody(email);

        return message;
    }

    private static string GetMessageId(EmailMessage email)
    {
        var messageId = SanitizeHeaderValue(email.MessageId, 200);
        if (string.IsNullOrEmpty(messageId))
        {
            messageId = $"<{Guid.NewGuid():N}.{DateTime.UtcNow.Ticks}@generated.local>";
        }

        return SanitizeHeaderValue(messageId, 200).Trim('<', '>');
    }

    private static void AddRecipients(InternetAddressList list, IEnumerable<Character> participants)
    {
        foreach (var participant in participants)
        {
            var address = TryCreateMailboxAddress(participant.FullName, participant.Email);
            if (address != null)
            {
                list.Add(address);
            }
        }
    }

    private static void ApplyThreadingHeaders(MimeMessage message, EmailMessage email)
    {
        var inReplyTo = SanitizeHeaderValue(email.InReplyTo, 200);
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo.Trim('<', '>');
        }

        foreach (var reference in email.References)
        {
            var sanitizedReference = SanitizeHeaderValue(reference, 200).Trim('<', '>');
            if (!string.IsNullOrEmpty(sanitizedReference))
            {
                message.References.Add(sanitizedReference);
            }
        }
    }

    private static MimeEntity BuildBody(EmailMessage email)
    {
        var builder = new BodyBuilder
        {
            HtmlBody = string.IsNullOrEmpty(email.BodyHtml)
                ? HtmlEmailFormatter.ConvertToHtml(email.BodyPlain)
                : email.BodyHtml
        };

        AddAttachments(builder, email.Attachments);
        return builder.ToMessageBody();
    }

    private static void AddAttachments(BodyBuilder builder, IEnumerable<EvidenceFoundry.Models.Attachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            if (attachment.Content == null)
                continue;

            var mimeType = ContentType.Parse(attachment.MimeType);
            var mimePart = new MimePart(mimeType)
            {
                Content = new MimeContent(new MemoryStream(attachment.Content)),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = attachment.FileName
            };

            if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
                mimePart.ContentId = attachment.ContentId;
                builder.LinkedResources.Add(mimePart);
            }
            else
            {
                mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);
                builder.Attachments.Add(mimePart);
            }
        }
    }

    private async Task<string> ProcessEmailAsync(
        EmailMessage email,
        string outputFolder,
        bool organizeBySender,
        bool releaseAttachmentContent,
        CancellationToken ct)
    {
        var fileName = FileNameHelper.GenerateEmlFileName(email);
        email.GeneratedFileName = fileName;

        var targetFolder = GetTargetFolder(email, outputFolder, organizeBySender);
        await SaveEmailAsEmlAsync(email, targetFolder, ct);

        if (releaseAttachmentContent)
        {
            ReleaseAttachmentContent(email);
        }

        return fileName;
    }

    private static string GetTargetFolder(EmailMessage email, string outputFolder, bool organizeBySender)
    {
        if (!organizeBySender || string.IsNullOrEmpty(email.From?.Email))
            return outputFolder;

        var senderFolder = SanitizeFolderName(email.From.Email);
        var targetFolder = Path.Combine(outputFolder, senderFolder);
        Directory.CreateDirectory(targetFolder);
        return targetFolder;
    }

    private static void ReleaseAttachmentContent(EmailMessage email)
    {
        if (email.Attachments.Count == 0)
            return;

        foreach (var attachment in email.Attachments)
        {
            attachment.Content = null;
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
