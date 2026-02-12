using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class ThreadSafeRandomTests
{
    [Fact]
    public void ThreadSafeRandomWithSameSeedIsDeterministic()
    {
        var rng1 = new ThreadSafeRandom(12345);
        var rng2 = new ThreadSafeRandom(12345);

        var values1 = Enumerable.Range(0, 5).Select(_ => rng1.Next()).ToArray();
        var values2 = Enumerable.Range(0, 5).Select(_ => rng2.Next()).ToArray();

        Assert.Equal(values1, values2);
    }

    [Fact]
    public void ThreadSafeRandomNextDoubleIsDeterministic()
    {
        var rng1 = new ThreadSafeRandom(987);
        var rng2 = new ThreadSafeRandom(987);

        var values1 = Enumerable.Range(0, 5).Select(_ => rng1.NextDouble()).ToArray();
        var values2 = Enumerable.Range(0, 5).Select(_ => rng2.NextDouble()).ToArray();

        Assert.Equal(values1, values2);
    }
}
