using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class DeterministicIdHelperTests
{
    [Fact]
    public void CreateGuid_SameInputs_ReturnsSameGuid()
    {
        var first = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");
        var second = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateGuid_DifferentInputs_ReturnsDifferentGuid()
    {
        var first = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");
        var second = DeterministicIdHelper.CreateGuid("storyline", "Title", "Different");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateShortToken_RespectsLengthAndIsDeterministic()
    {
        var tokenA = DeterministicIdHelper.CreateShortToken("image-file", 8, "seed");
        var tokenB = DeterministicIdHelper.CreateShortToken("image-file", 8, "seed");

        Assert.Equal(8, tokenA.Length);
        Assert.Equal(tokenA, tokenB);
    }
}
