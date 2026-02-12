using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class ClockTests
{
    [Fact]
    public void EnsureKindUnspecifiedDefaultsToUtc()
    {
        var input = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var result = Clock.EnsureKind(input, DateTimeKind.Utc);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(input.Ticks, result.Ticks);
    }

    [Fact]
    public void EnsureKindPreservesExistingKind()
    {
        var local = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var localResult = Clock.EnsureKind(local, DateTimeKind.Utc);

        Assert.Equal(DateTimeKind.Local, localResult.Kind);
        Assert.Equal(local.Ticks, localResult.Ticks);

        var utc = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var utcResult = Clock.EnsureKind(utc, DateTimeKind.Local);

        Assert.Equal(DateTimeKind.Utc, utcResult.Kind);
        Assert.Equal(utc.Ticks, utcResult.Ticks);
    }

    [Fact]
    public void EnsureOffsetUnspecifiedUsesDefaultKind()
    {
        var input = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
        var offset = Clock.EnsureOffset(input, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, offset.Offset);
        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), offset.UtcDateTime);
    }
}
