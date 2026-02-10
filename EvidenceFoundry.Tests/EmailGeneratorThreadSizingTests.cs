using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorThreadSizingTests
{
    [Fact]
    public void BuildThreadSizePlan_SumsToTotalAndRespectsMax()
    {
        var rng = new Random(1234);
        var sizes = DateHelper.BuildThreadSizePlan(57, rng);

        Assert.Equal(57, sizes.Sum());
        Assert.All(sizes, size => Assert.InRange(size, 1, 50));
    }

    [Fact]
    public void BuildThreadSizePlan_IsDeterministicForSeed()
    {
        var rng1 = new Random(42);
        var rng2 = new Random(42);

        var sizes1 = DateHelper.BuildThreadSizePlan(400, rng1);
        var sizes2 = DateHelper.BuildThreadSizePlan(400, rng2);

        Assert.Equal(sizes1, sizes2);
    }

    [Fact]
    public void BuildThreadSizePlan_ReturnsEmptyForZero()
    {
        var rng = new Random(1);
        var sizes = DateHelper.BuildThreadSizePlan(0, rng);
        Assert.Empty(sizes);
    }

    [Fact]
    public void BuildThreadSizePlan_ThrowsOnNegativeTotal()
    {
        var rng = new Random(1);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DateHelper.BuildThreadSizePlan(-1, rng));
        Assert.Contains("non-negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
