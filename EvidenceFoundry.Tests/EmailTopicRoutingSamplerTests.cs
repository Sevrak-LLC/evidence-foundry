using EvidenceFoundry.Services;
using Xunit;

namespace EvidenceFoundry.Tests;

public class EmailTopicRoutingSamplerTests
{
    [Fact]
    public void SampleWeightedTopicsReturnsAllWhenSampleCountExceedsCandidates()
    {
        var candidates = new List<int> { 1, 2, 3 };
        var tiers = new Dictionary<int, EmailGenerator.TopicTier>
        {
            [1] = EmailGenerator.TopicTier.Core,
            [2] = EmailGenerator.TopicTier.Department,
            [3] = EmailGenerator.TopicTier.Role
        };

        var sampled = EmailGenerator.SampleWeightedTopics(candidates, tiers, 5, new Random(42));

        Assert.Equal(candidates, sampled);
    }

    [Fact]
    public void SampleWeightedTopicsRespectsWeightThresholds()
    {
        var candidates = new List<int> { 10, 20 };
        var tiers = new Dictionary<int, EmailGenerator.TopicTier>
        {
            [10] = EmailGenerator.TopicTier.Core,
            [20] = EmailGenerator.TopicTier.Role
        };

        var rng = new FixedRandom(0.25);
        var sampled = EmailGenerator.SampleWeightedTopics(candidates, tiers, 1, rng);

        Assert.Single(sampled);
        Assert.Equal(20, sampled[0]);
    }

    private sealed class FixedRandom : Random
    {
        private readonly Queue<double> _values;

        public FixedRandom(params double[] values)
        {
            _values = new Queue<double>(values);
        }

        public override double NextDouble()
        {
            if (_values.Count == 0)
                return 0;
            return _values.Dequeue();
        }
    }
}
