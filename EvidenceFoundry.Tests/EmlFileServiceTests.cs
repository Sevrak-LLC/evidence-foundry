using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmlFileServiceTests
{
    [Fact]
    public async Task SaveAllEmailsAsync_Parallel_SavesAllEmailsAndReleasesAttachments()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EvidenceFoundry.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var threads = BuildThreads(out var emails);
            var service = new EmlFileService();

            await service.SaveAllEmailsAsync(
                threads,
                tempDir,
                organizeBySender: false,
                progress: null,
                ct: default,
                maxDegreeOfParallelism: 4,
                releaseAttachmentContent: true);

            var files = Directory.GetFiles(tempDir, "*.eml", SearchOption.AllDirectories);
            Assert.Equal(emails.Count, files.Length);

            foreach (var email in emails)
            {
                foreach (var attachment in email.Attachments)
                {
                    Assert.Null(attachment.Content);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveAllEmailsAsync_OrganizeBySender_CreatesSenderSubfolders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EvidenceFoundry.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var threads = BuildThreads(out var emails);
            var service = new EmlFileService();

            await service.SaveAllEmailsAsync(
                threads,
                tempDir,
                organizeBySender: true,
                progress: null,
                ct: default,
                maxDegreeOfParallelism: 2);

            var expectedFolders = emails
                .Select(e => SanitizeFolderName(e.From.Email))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(folder => Path.Combine(tempDir, folder))
                .ToList();

            foreach (var folder in expectedFolders)
            {
                Assert.True(Directory.Exists(folder));
            }

            var files = Directory.GetFiles(tempDir, "*.eml", SearchOption.AllDirectories);
            Assert.Equal(emails.Count, files.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SaveThreadEmailsAsync_SavesThreadAndReleasesAttachments()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EvidenceFoundry.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var threads = BuildThreads(out var emails);
            var thread = threads.Single();
            var service = new EmlFileService();

            await service.SaveThreadEmailsAsync(
                thread,
                tempDir,
                organizeBySender: false,
                progress: null,
                ct: default,
                releaseAttachmentContent: true);

            var files = Directory.GetFiles(tempDir, "*.eml", SearchOption.AllDirectories);
            Assert.Equal(emails.Count, files.Length);

            foreach (var email in emails)
            {
                foreach (var attachment in email.Attachments)
                {
                    Assert.Null(attachment.Content);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static List<EmailThread> BuildThreads(out List<EmailMessage> emails)
    {
        var senderA = new Character
        {
            FirstName = "Alex",
            LastName = "Smith",
            Email = "alex.smith@contoso.com",
            SignatureBlock = "Alex Smith\nContoso"
        };
        var senderB = new Character
        {
            FirstName = "Jamie",
            LastName = "Lee",
            Email = "jamie.lee@fabrikam.com",
            SignatureBlock = "Jamie Lee\nFabrikam"
        };

        emails = new List<EmailMessage>
        {
            CreateEmail(senderA, senderB, "Subject A", "Hello from Alex."),
            CreateEmail(senderB, senderA, "Subject B", "Reply from Jamie."),
            CreateEmail(senderA, senderB, "Subject C", "Follow-up from Alex.")
        };

        var thread = new EmailThread
        {
            EmailMessages = emails
        };

        return new List<EmailThread> { thread };
    }

    private static EmailMessage CreateEmail(Character from, Character to, string subject, string body)
    {
        var attachment = new Attachment
        {
            Type = AttachmentType.Image,
            FileName = "test.png",
            ContentDescription = "Test attachment",
            Content = new byte[] { 1, 2, 3, 4 }
        };

        return new EmailMessage
        {
            From = from,
            To = new List<Character> { to },
            Subject = subject,
            BodyPlain = body + "\n\n" + from.SignatureBlock,
            SentDate = DateTime.UtcNow,
            Attachments = new List<Attachment> { attachment }
        };
    }

    private static string SanitizeFolderName(string email)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = email;
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized;
    }
}
