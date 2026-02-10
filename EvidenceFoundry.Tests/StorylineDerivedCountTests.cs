using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class StorylineDerivedCountTests
{
    [Fact]
    public void EmailCount_IsSumOfBeatEmailCounts()
    {
        var storyline = new Storyline
        {
            Beats = new List<StoryBeat>
            {
                new() { EmailCount = 3 },
                new() { EmailCount = 5 }
            }
        };

        Assert.Equal(8, storyline.EmailCount);
    }

    [Fact]
    public void ThreadCount_IsSumOfBeatThreadCounts()
    {
        var beat1 = new StoryBeat
        {
            Threads = new List<EmailThread> { new(), new() }
        };
        var beat2 = new StoryBeat
        {
            Threads = new List<EmailThread> { new() }
        };

        var storyline = new Storyline
        {
            Beats = new List<StoryBeat> { beat1, beat2 }
        };

        Assert.Equal(3, storyline.ThreadCount);
    }
}
