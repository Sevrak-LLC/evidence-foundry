using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailThreadGeneratorTests
{
    [Fact]
    public void EnsurePlaceholderMessages_CreatesPlaceholdersWhenEmpty()
    {
        var generator = new EmailThreadGenerator();
        var storyBeatId = Guid.NewGuid();
        var storylineId = Guid.NewGuid();
        var thread = new EmailThread
        {
            StoryBeatId = storyBeatId,
            StorylineId = storylineId
        };

        generator.EnsurePlaceholderMessages(thread, 3);

        Assert.Equal(3, thread.EmailMessages.Count);
        for (var i = 0; i < thread.EmailMessages.Count; i++)
        {
            Assert.Equal(thread.Id, thread.EmailMessages[i].EmailThreadId);
            Assert.Equal(storyBeatId, thread.EmailMessages[i].StoryBeatId);
            Assert.Equal(storylineId, thread.EmailMessages[i].StorylineId);
            Assert.Equal(i, thread.EmailMessages[i].SequenceInThread);
        }
    }

    [Fact]
    public void EnsurePlaceholderMessages_ThrowsWhenExistingCountDoesNotMatch()
    {
        var generator = new EmailThreadGenerator();
        var thread = new EmailThread
        {
            EmailMessages = new List<EmailMessage> { new() }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => generator.EnsurePlaceholderMessages(thread, 2));

        Assert.Contains("placeholder count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResetThreadForRetry_RebuildsMessages()
    {
        var generator = new EmailThreadGenerator();
        var thread = new EmailThread
        {
            EmailMessages = new List<EmailMessage> { new() }
        };

        generator.ResetThreadForRetry(thread, 2);

        Assert.Equal(2, thread.EmailMessages.Count);
        Assert.All(thread.EmailMessages, message => Assert.Equal(thread.Id, message.EmailThreadId));
    }
}
