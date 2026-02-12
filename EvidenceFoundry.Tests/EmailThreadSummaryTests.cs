using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class EmailThreadSummaryTests
{
    [Fact]
    public void GetSummaryUsesDisplaySubjectAndMessageCount()
    {
        var thread = new EmailThread
        {
            Id = Guid.NewGuid(),
            Relevance = EmailThread.ThreadRelevance.Responsive,
            IsHot = true,
            Scope = EmailThreadScope.External
        };
        thread.AddEmailMessage(new EmailMessage { Subject = "Update" });
        thread.AddEmailMessage(new EmailMessage { Subject = "Re: Update" });

        var summary = thread.GetSummary();

        Assert.Equal(thread.Id, summary.Id);
        Assert.Equal("Update", summary.Subject);
        Assert.Equal(2, summary.MessageCount);
        Assert.Equal(EmailThread.ThreadRelevance.Responsive, summary.Relevance);
        Assert.True(summary.IsHot);
        Assert.Equal(EmailThreadScope.External, summary.Scope);
    }

    [Fact]
    public void GetSummaryAllowsEmptySubject()
    {
        var thread = new EmailThread();

        var summary = thread.GetSummary();

        Assert.Equal(Guid.Empty, summary.Id);
        Assert.Equal(string.Empty, summary.Subject);
        Assert.Equal(0, summary.MessageCount);
    }
}
