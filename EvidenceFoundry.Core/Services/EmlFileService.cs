using System.Diagnostics;
using MimeKit;
using EvidenceFoundry.Models;
using EvidenceFoundry.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EvidenceFoundry.Services;

public class EmlFileService
{
    private const int MaxFileNameLength = 180;
    private readonly ILogger<EmlFileService> _logger;

    public EmlFileService(ILogger<EmlFileService>? logger = null)
    {
        _logger = logger ?? NullLogger<EmlFileService>.Instance;
        _logger.LogDebug("EmlFileService initialized.");
    }

    public static async Task SaveEmailAsEmlAsync(
        EmailMessage email,
        string outputFolder,
        CancellationToken ct = default)
        => await SaveEmailAsEmlAsync(email, outputFolder, NullLogger<EmlFileService>.Instance, ct);

    public static async Task SaveEmailAsEmlAsync(
        EmailMessage email,
        string outputFolder,
        ILogger<EmlFileService>? logger,
        CancellationToken ct = default)
    {
        var log = logger ?? NullLogger<EmlFileService>.Instance;
        log.LogDebug("Saving single email to EML output folder.");

        ArgumentNullException.ThrowIfNull(email);
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output folder is required.", nameof(outputFolder));

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
        int? maxDegreeOfParallelism = null,
        bool releaseAttachmentContent = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(threads);
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output folder is required.", nameof(outputFolder));

        var stopwatch = Stopwatch.StartNew();
        // Ensure output folder exists
        Directory.CreateDirectory(outputFolder);

        var allEmails = threads.SelectMany(t => t.EmailMessages).ToList();
        var total = allEmails.Count;
        var completed = 0;

        var degree = Math.Max(1, maxDegreeOfParallelism ?? 1);
        var shouldLog = _logger.IsEnabled(LogLevel.Information) && (threads.Count > 1 || total > 50);
        if (shouldLog)
        {
            _logger.LogInformation(
                "Saving {EmailCount} emails across {ThreadCount} thread(s) to {OutputFolder} with degree {Degree}.",
                total,
                threads.Count,
                outputFolder,
                degree);
        }

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

        if (shouldLog)
        {
            _logger.LogInformation(
                "Saved {EmailCount} emails in {DurationMs} ms.",
                total,
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static MimeMessage CreateMessage(EmailMessage email)
    {
        var message = new MimeMessage
        {
            MessageId = GetMessageId(email),
            Date = Clock.EnsureOffset(email.SentDate, DateTimeKind.Local),
            Subject = HeaderValueHelper.SanitizeHeaderValue(email.Subject, 255)
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
        var messageId = HeaderValueHelper.SanitizeHeaderValue(email.MessageId, 200);
        if (string.IsNullOrEmpty(messageId))
        {
            var domain = ResolveMessageIdDomain(email);
            messageId = ThreadingHelper.GenerateMessageId(email, domain);
        }

        return HeaderValueHelper.SanitizeHeaderValue(messageId, 200).Trim('<', '>');
    }

    private static string ResolveMessageIdDomain(EmailMessage email)
    {
        if (!string.IsNullOrWhiteSpace(email.From?.Domain))
            return email.From.Domain;

        if (!string.IsNullOrWhiteSpace(email.From?.Email))
        {
            var atIndex = email.From.Email.IndexOf('@');
            if (atIndex >= 0 && atIndex < email.From.Email.Length - 1)
                return email.From.Email[(atIndex + 1)..];
        }

        return "generated.local";
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
        var inReplyTo = HeaderValueHelper.SanitizeHeaderValue(email.InReplyTo, 200);
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo.Trim('<', '>');
        }

        foreach (var reference in email.References)
        {
            var sanitizedReference = HeaderValueHelper.SanitizeHeaderValue(reference, 200).Trim('<', '>');
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
        bool releaseAttachmentContent = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (string.IsNullOrWhiteSpace(outputFolder))
            throw new ArgumentException("Output folder is required.", nameof(outputFolder));

        return SaveAllEmailsAsync(
            new List<EmailThread> { thread },
            outputFolder,
            organizeBySender,
            progress,
            maxDegreeOfParallelism: 1,
            releaseAttachmentContent: releaseAttachmentContent,
            ct: ct);
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
        var displayName = HeaderValueHelper.SanitizeHeaderText(from.FullName, 128);
        if (EmailAddressHelper.TryNormalizeEmail(from.Email, out var normalized))
        {
            return new MailboxAddress(displayName, normalized);
        }

        var fallbackName = string.IsNullOrWhiteSpace(displayName) ? "Unknown Sender" : displayName;
        return new MailboxAddress(fallbackName, "unknown@invalid.local");
    }

    private static MailboxAddress? TryCreateMailboxAddress(string? displayName, string? email)
    {
        if (!EmailAddressHelper.TryNormalizeEmail(email, out var normalized))
        {
            return null;
        }

        var safeName = HeaderValueHelper.SanitizeHeaderText(displayName, 128);
        return new MailboxAddress(safeName, normalized);
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
