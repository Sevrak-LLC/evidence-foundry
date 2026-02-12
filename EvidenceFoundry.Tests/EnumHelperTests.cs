using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class EnumHelperTests
{
    [Fact]
    public void HumanizeEnumNameInsertsSpacesAtBoundaries()
    {
        var result = EnumHelper.HumanizeEnumName("PublicBenefitCorporation");

        Assert.Equal("Public Benefit Corporation", result);
    }

    [Fact]
    public void TryParseEnumReturnsFalseForNull()
    {
        var parsed = EnumHelper.TryParseEnum<UsState>(null, out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseEnumParsesIgnoreCase()
    {
        var parsed = EnumHelper.TryParseEnum("california", out UsState result);

        Assert.True(parsed);
        Assert.Equal(UsState.California, result);
    }
}
