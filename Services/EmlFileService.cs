using MimeKit;
using ReelDiscovery.Models;
using ReelDiscovery.Helpers;

namespace ReelDiscovery.Services;

public class EmlFileService
{
    public async Task SaveEmailAsEmlAsync(
        EmailMessage email,
        string outputFolder,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();

        // Set Message-ID (required for threading)
        // If MessageId is empty, generate a new one
        var messageId = email.MessageId;
        if (string.IsNullOrEmpty(messageId))
        {
            messageId = $"<{Guid.NewGuid():N}.{DateTime.UtcNow.Ticks}@generated.local>";
        }
        message.MessageId = messageId.Trim('<', '>');

        // Set date
        message.Date = new DateTimeOffset(email.SentDate);

        // Set subject
        message.Subject = email.Subject;

        // From
        message.From.Add(new MailboxAddress(email.From.FullName, email.From.Email));

        // To
        foreach (var to in email.To)
        {
            message.To.Add(new MailboxAddress(to.FullName, to.Email));
        }

        // Cc
        foreach (var cc in email.Cc)
        {
            message.Cc.Add(new MailboxAddress(cc.FullName, cc.Email));
        }

        // Threading headers
        if (!string.IsNullOrEmpty(email.InReplyTo))
        {
            message.InReplyTo = email.InReplyTo.Trim('<', '>');
        }

        if (email.References.Count > 0)
        {
            foreach (var reference in email.References)
            {
                message.References.Add(reference.Trim('<', '>'));
            }
        }

        // Build body
        var builder = new BodyBuilder
        {
            TextBody = email.BodyPlain
        };

        // Add HTML body if present
        if (!string.IsNullOrEmpty(email.BodyHtml))
        {
            builder.HtmlBody = email.BodyHtml;
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
        var fileName = email.GeneratedFileName ?? FileNameHelper.GenerateEmlFileName(email);
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
        CancellationToken ct = default)
    {
        // Ensure output folder exists
        Directory.CreateDirectory(outputFolder);

        var allEmails = threads.SelectMany(t => t.Messages).ToList();
        var total = allEmails.Count;
        var completed = 0;

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

            completed++;
            progress?.Report((completed, total, fileName));
        }
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
}
