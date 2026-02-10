using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Tests;

public class EnumHelperTests
{
    [Fact]
    public void HumanizeEnumName_InsertsSpacesAtBoundaries()
    {
        var result = EnumHelper.HumanizeEnumName("PublicBenefitCorporation");

        Assert.Equal("Public Benefit Corporation", result);
    }

    [Fact]
    public void TryParseEnum_ReturnsFalseForNull()
    {
        var parsed = EnumHelper.TryParseEnum<UsState>(null, out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseEnum_ParsesIgnoreCase()
    {
        var parsed = EnumHelper.TryParseEnum("california", out UsState result);

        Assert.True(parsed);
        Assert.Equal(UsState.California, result);
    }
}
