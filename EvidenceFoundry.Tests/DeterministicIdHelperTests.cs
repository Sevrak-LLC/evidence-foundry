using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class DeterministicIdHelperTests
{
    [Fact]
    public void CreateGuidSameInputsReturnsSameGuid()
    {
        var first = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");
        var second = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateGuidDifferentInputsReturnsDifferentGuid()
    {
        var first = DeterministicIdHelper.CreateGuid("storyline", "Title", "Summary");
        var second = DeterministicIdHelper.CreateGuid("storyline", "Title", "Different");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateShortTokenRespectsLengthAndIsDeterministic()
    {
        var tokenA = DeterministicIdHelper.CreateShortToken("image-file", 8, "seed");
        var tokenB = DeterministicIdHelper.CreateShortToken("image-file", 8, "seed");

        Assert.Equal(8, tokenA.Length);
        Assert.Equal(tokenA, tokenB);
    }
}
