using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ThreadGraphTests
{
    [Fact]
    public void Build_GeneratesParentChildLinksAndChronologicalOrder()
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Ops"
        };

        var generator = new EmailThreadGenerator();
        generator.EnsurePlaceholderMessages(thread, 3);

        var emails = thread.EmailMessages.ToList();
        emails[0].SentDate = new DateTime(2024, 3, 1, 9, 0, 0);
        emails[1].SentDate = new DateTime(2024, 3, 1, 10, 0, 0);
        emails[2].SentDate = new DateTime(2024, 3, 1, 9, 30, 0);

        emails[1].ParentEmailId = emails[0].Id;
        emails[2].ParentEmailId = emails[0].Id;

        var graph = ThreadGraph.Build(thread);

        Assert.True(graph.ChildrenByParent.TryGetValue(emails[0].Id, out var children));
        Assert.Equal(2, children.Count);
        Assert.Contains(emails[1].Id, children);
        Assert.Contains(emails[2].Id, children);

        var chronological = graph.ChronologicalOrder;
        Assert.Equal(emails[0].Id, chronological[0]);
        Assert.Equal(emails[2].Id, chronological[1]);
        Assert.Equal(emails[1].Id, chronological[2]);
    }
}
