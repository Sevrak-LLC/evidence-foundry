using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class WizardStateTests
{
    [Fact]
    public void GetActiveStorylines_ReturnsEmptyWhenNoStoryline()
    {
        var state = new WizardState();

        var active = state.GetActiveStorylines().ToList();

        Assert.Empty(active);
    }

    [Fact]
    public void GetActiveStorylines_ReturnsStorylineWhenSet()
    {
        var state = new WizardState();
        var storyline = new Storyline { Title = "First" };
        state.Storyline = storyline;

        var active = state.GetActiveStorylines().ToList();

        Assert.Single(active);
        Assert.Equal(storyline.Id, active[0].Id);
    }

    [Fact]
    public void TopicDisplayName_UsesSelectionWhenTopicEmpty()
    {
        var state = new WizardState
        {
            CaseArea = "Commercial",
            MatterType = "Contracts",
            Issue = "Breach"
        };

        Assert.Equal("Commercial > Contracts > Breach", state.TopicDisplayName);
    }

    [Fact]
    public void StorylineIssueDescription_FallsBackToTopic()
    {
        var state = new WizardState
        {
            Topic = "Commercial > Contracts > Breach"
        };

        Assert.Equal("Commercial > Contracts > Breach", state.StorylineIssueDescription);
    }

    [Fact]
    public void GetGenerationSummary_ReturnsZerosWhenNoSelection()
    {
        var state = new WizardState();

        var summary = state.GetGenerationSummary();

        Assert.Equal(0, summary.BeatCount);
        Assert.Equal(0, summary.ThreadCount);
        Assert.Equal(0, summary.HotThreadCount);
        Assert.Equal(0, summary.RelevantThreadCount);
        Assert.Equal(0, summary.NonRelevantThreadCount);
        Assert.Equal(0, summary.EmailCount);
        Assert.Equal(0, summary.EstimatedDocumentAttachments);
        Assert.Equal(0, summary.EstimatedImageAttachments);
        Assert.Equal(0, summary.EstimatedVoicemailAttachments);
        Assert.Equal(0, summary.EstimatedCalendarInviteChecks);
        Assert.Null(summary.StartDate);
        Assert.Null(summary.EndDate);
    }

    [Fact]
    public void GetGenerationSummary_EstimatesAttachments()
    {
        var storyline = new Storyline
        {
            Title = "Test",
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 10)
        };
        storyline.Beats.Add(new StoryBeat
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 5),
            EmailCount = 12,
            Threads = new List<EmailThread>
            {
                new EmailThread
                {
                    IsHot = true,
                    Relevance = EmailThread.ThreadRelevance.NonResponsive
                },
                new EmailThread
                {
                    Relevance = EmailThread.ThreadRelevance.Responsive
                }
            }
        });
        storyline.Beats.Add(new StoryBeat
        {
            StartDate = new DateTime(2025, 1, 6),
            EndDate = new DateTime(2025, 1, 10),
            EmailCount = 8,
            Threads = new List<EmailThread> { new EmailThread() }
        });

        var state = new WizardState
        {
            Storyline = storyline
        };

        state.Config.AttachmentPercentage = 20;
        state.Config.IncludeWord = true;
        state.Config.IncludeExcel = false;
        state.Config.IncludePowerPoint = false;
        state.Config.IncludeImages = true;
        state.Config.ImagePercentage = 10;
        state.Config.IncludeVoicemails = true;
        state.Config.VoicemailPercentage = 5;
        state.Config.IncludeCalendarInvites = true;
        state.Config.CalendarInvitePercentage = 10;

        var summary = state.GetGenerationSummary();

        Assert.Equal(2, summary.BeatCount);
        Assert.Equal(3, summary.ThreadCount);
        Assert.Equal(1, summary.HotThreadCount);
        Assert.Equal(1, summary.RelevantThreadCount);
        Assert.Equal(1, summary.NonRelevantThreadCount);
        Assert.Equal(20, summary.EmailCount);
        Assert.Equal(4, summary.EstimatedDocumentAttachments);
        Assert.Equal(2, summary.EstimatedImageAttachments);
        Assert.Equal(1, summary.EstimatedVoicemailAttachments);
        Assert.Equal(2, summary.EstimatedCalendarInviteChecks);
        Assert.Equal(new DateTime(2025, 1, 1), summary.StartDate);
        Assert.Equal(new DateTime(2025, 1, 10), summary.EndDate);
    }

    [Fact]
    public void GetGenerationSummary_DocsZeroWhenNoAttachmentTypes()
    {
        var storyline = new Storyline
        {
            Title = "Test",
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 2)
        };
        storyline.Beats.Add(new StoryBeat
        {
            StartDate = new DateTime(2025, 1, 1),
            EndDate = new DateTime(2025, 1, 2),
            EmailCount = 10
        });

        var state = new WizardState
        {
            Storyline = storyline
        };

        state.Config.AttachmentPercentage = 50;
        state.Config.IncludeWord = false;
        state.Config.IncludeExcel = false;
        state.Config.IncludePowerPoint = false;

        var summary = state.GetGenerationSummary();

        Assert.Equal(0, summary.EstimatedDocumentAttachments);
    }
}
