using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class GenerationProgressTests
{
    [Fact]
    public void Snapshot_CopiesAllFields()
    {
        var progress = new GenerationProgress
        {
            TotalEmails = 120,
            CompletedEmails = 45,
            TotalAttachments = 30,
            CompletedAttachments = 12,
            TotalImages = 8,
            CompletedImages = 3,
            CurrentOperation = "Generating attachments",
            CurrentStoryline = "Budget review"
        };

        var snapshot = progress.Snapshot();

        Assert.NotSame(progress, snapshot);
        Assert.Equal(progress.TotalEmails, snapshot.TotalEmails);
        Assert.Equal(progress.CompletedEmails, snapshot.CompletedEmails);
        Assert.Equal(progress.TotalAttachments, snapshot.TotalAttachments);
        Assert.Equal(progress.CompletedAttachments, snapshot.CompletedAttachments);
        Assert.Equal(progress.TotalImages, snapshot.TotalImages);
        Assert.Equal(progress.CompletedImages, snapshot.CompletedImages);
        Assert.Equal(progress.CurrentOperation, snapshot.CurrentOperation);
        Assert.Equal(progress.CurrentStoryline, snapshot.CurrentStoryline);

        progress.CompletedEmails = 99;
        progress.CurrentOperation = "Changed";

        Assert.Equal(45, snapshot.CompletedEmails);
        Assert.Equal("Generating attachments", snapshot.CurrentOperation);
    }
}
