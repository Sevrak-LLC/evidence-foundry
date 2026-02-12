using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ThreadingHelperTests
{
    [Fact]
    public void SetupThreadingUsesParentMessageForInReplyTo()
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Ops"
        };

        EmailThreadGenerator.EnsurePlaceholderMessages(thread, 3);

        var sender = new Character { FirstName = "Jane", LastName = "Doe", Email = "jane@corp.com" };
        var receiver = new Character { FirstName = "Max", LastName = "Rowe", Email = "max@corp.com" };

        var emails = thread.EmailMessages.ToList();
        emails[0].From = sender;
        emails[0].SetTo(new[] { receiver });
        emails[0].Subject = "Status";
        emails[0].SentDate = new DateTime(2024, 5, 1, 9, 0, 0);
        emails[0].ParentEmailId = null;

        emails[1].From = receiver;
        emails[1].SetTo(new[] { sender });
        emails[1].Subject = ThreadingHelper.AddReplyPrefix("Status");
        emails[1].SentDate = new DateTime(2024, 5, 1, 10, 0, 0);
        emails[1].ParentEmailId = emails[0].Id;

        emails[2].From = sender;
        emails[2].SetTo(new[] { receiver });
        emails[2].Subject = ThreadingHelper.AddReplyPrefix("Status");
        emails[2].SentDate = new DateTime(2024, 5, 1, 10, 30, 0);
        emails[2].ParentEmailId = emails[0].Id;

        ThreadingHelper.SetupThreading(thread, "corp.com");

        Assert.Equal(emails[0].MessageId, emails[1].InReplyTo);
        Assert.Equal(emails[0].MessageId, emails[2].InReplyTo);
    }
}
