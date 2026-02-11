using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class StoryBeatGeneratorTests
{
    [Fact]
    public void ValidateStoryBeats_AllowsStrictlySequentialBeats()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 1, 3) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 4), EndDate = new DateTime(2025, 1, 6) },
            new() { Name = "Beat 3", StartDate = new DateTime(2025, 1, 7), EndDate = new DateTime(2025, 1, 10) }
        };

        StoryBeatGenerator.ValidateStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 10));
    }

    [Fact]
    public void ValidateStoryBeats_RejectsSharedBoundary()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 1, 3) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 3), EndDate = new DateTime(2025, 1, 5) }
        };

        Assert.Throws<InvalidOperationException>(() =>
            StoryBeatGenerator.ValidateStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 5)));
    }

    [Fact]
    public void ValidateStoryBeats_RejectsMismatchedStartOrEnd()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 2), EndDate = new DateTime(2025, 1, 3) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 4), EndDate = new DateTime(2025, 1, 6) }
        };

        Assert.Throws<InvalidOperationException>(() =>
            StoryBeatGenerator.ValidateStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 6)));
    }

    [Fact]
    public void NormalizeStoryBeats_FixesOffByOneBoundaries()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 2), EndDate = new DateTime(2025, 1, 4) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 5), EndDate = new DateTime(2025, 1, 8) }
        };

        var changed = DateHelper.NormalizeStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 9));

        Assert.True(changed);
        StoryBeatGenerator.ValidateStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 9));
    }

    [Fact]
    public void NormalizeStoryBeats_FixesSingleDayOverlap()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 1, 3) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 3), EndDate = new DateTime(2025, 1, 6) }
        };

        var changed = DateHelper.NormalizeStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 6));

        Assert.True(changed);
        StoryBeatGenerator.ValidateStoryBeats(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 6));
    }

    [Fact]
    public void FindFirstInvalidBeatIndex_FindsEarliestIssue()
    {
        var beats = new List<StoryBeat>
        {
            new() { Name = "Beat 1", StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 1, 3) },
            new() { Name = "Beat 2", StartDate = new DateTime(2025, 1, 3), EndDate = new DateTime(2025, 1, 5) },
            new() { Name = "Beat 3", StartDate = new DateTime(2025, 1, 6), EndDate = new DateTime(2025, 1, 7) }
        };

        var index = StoryBeatGenerator.FindFirstInvalidBeatIndex(beats, new DateTime(2025, 1, 1), new DateTime(2025, 1, 7));

        Assert.Equal(1, index);
    }
}
