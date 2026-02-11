using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class StoryBeatPlanningTests
{
    [Fact]
    public void PlanEmailThreadsForBeats_CreatesPlaceholdersThatMatchCounts()
    {
        var generator = new EmailThreadGenerator();
        var storylineId = Guid.NewGuid();
        var beats = new List<StoryBeat>
        {
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 1",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 2)
            },
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 2",
                StartDate = new DateTime(2024, 1, 3),
                EndDate = new DateTime(2024, 1, 4)
            }
        };

        var rng = new Random(123);
        generator.PlanEmailThreadsForBeats(beats, 3, rng);

        foreach (var beat in beats)
        {
            if (beat.EmailCount == 0)
            {
                Assert.Empty(beat.Threads);
                continue;
            }

            Assert.Equal(beat.EmailCount, beat.Threads.Sum(t => t.EmailMessages.Count));

            foreach (var thread in beat.Threads)
            {
                Assert.Equal(beat.Id, thread.StoryBeatId);
                Assert.Equal(storylineId, thread.StorylineId);
                Assert.InRange(thread.EmailMessages.Count, 1, 50);

                for (var i = 0; i < thread.EmailMessages.Count; i++)
                {
                    var message = thread.EmailMessages[i];
                    Assert.Equal(thread.Id, message.EmailThreadId);
                    Assert.Equal(thread.StoryBeatId, message.StoryBeatId);
                    Assert.Equal(thread.StorylineId, message.StorylineId);
                    Assert.Equal(i, message.SequenceInThread);
                }
            }
        }
    }

    [Fact]
    public void PlanEmailThreadsForBeats_ThrowsOnInvalidKeyRoleCount()
    {
        var generator = new EmailThreadGenerator();
        var storylineId = Guid.NewGuid();
        var beats = new List<StoryBeat>
        {
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 1",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 1)
            }
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            generator.PlanEmailThreadsForBeats(beats, 0, new Random(1)));

        Assert.Contains("Key role count", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanEmailThreadsForBeats_ThrowsWhenStorylineIdsMissing()
    {
        var generator = new EmailThreadGenerator();
        var beats = new List<StoryBeat>
        {
            new()
            {
                Name = "Beat 1",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 1)
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            generator.PlanEmailThreadsForBeats(beats, 1, new Random(1)));

        Assert.Contains("StorylineId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlanEmailThreadsForBeats_EnsuresResponsiveAndHotCoverage()
    {
        var generator = new EmailThreadGenerator();
        var storylineId = Guid.NewGuid();
        var beats = new List<StoryBeat>
        {
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 1",
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 1, 2)
            },
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 2",
                StartDate = new DateTime(2024, 1, 3),
                EndDate = new DateTime(2024, 1, 4)
            },
            new()
            {
                StorylineId = storylineId,
                Name = "Beat 3",
                StartDate = new DateTime(2024, 1, 5),
                EndDate = new DateTime(2024, 1, 6)
            }
        };

        var rng = new FixedRandom();
        generator.PlanEmailThreadsForBeats(beats, 3, rng);

        var beatsWithThreads = beats.Where(b => b.Threads.Count > 0).ToList();
        Assert.NotEmpty(beatsWithThreads);

        foreach (var beat in beatsWithThreads)
        {
            Assert.Contains(beat.Threads, t => t.IsHot || t.Relevance == EmailThread.ThreadRelevance.Responsive);
        }

        Assert.Contains(beatsWithThreads.SelectMany(b => b.Threads), t => t.IsHot);

        var middleBeat = beatsWithThreads[beatsWithThreads.Count / 2];
        Assert.Contains(middleBeat.Threads, t => t.IsHot);
    }

    private sealed class FixedRandom : Random
    {
        public override double NextDouble() => 1.0;

        public override int Next(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
                return minValue;

            return maxValue - 1;
        }

        public override int Next(int maxValue)
        {
            if (maxValue <= 0)
                return 0;

            return maxValue - 1;
        }

        public override int Next() => int.MaxValue - 1;
    }
}
