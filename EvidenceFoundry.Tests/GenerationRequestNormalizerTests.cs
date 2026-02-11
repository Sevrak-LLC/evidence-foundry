using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class GenerationRequestNormalizerTests
{
    [Fact]
    public void NormalizeIndustryPreference_ReturnsRandomForNull()
    {
        var result = GenerationRequestNormalizer.NormalizeIndustryPreference(null);

        Assert.Equal(GenerationRequestNormalizer.RandomIndustryPreference, result);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void NormalizeIndustryPreference_ReturnsRandomForWhitespace(string input)
    {
        var result = GenerationRequestNormalizer.NormalizeIndustryPreference(input);

        Assert.Equal(GenerationRequestNormalizer.RandomIndustryPreference, result);
    }

    [Theory]
    [InlineData("Random", "Random")]
    [InlineData("random", "Random")]
    [InlineData(" RANDOM ", "Random")]
    [InlineData("InformationTechnology", "InformationTechnology")]
    [InlineData(" InformationTechnology ", "InformationTechnology")]
    [InlineData("Randomly", "Randomly")]
    public void NormalizeIndustryPreference_TrimsAndNormalizesRandom(string input, string expected)
    {
        var result = GenerationRequestNormalizer.NormalizeIndustryPreference(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(10, 3)]
    public void NormalizePartyCount_ClampsWithinRange(int input, int expected)
    {
        var result = GenerationRequestNormalizer.NormalizePartyCount(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Random", true)]
    [InlineData("random", true)]
    [InlineData("RANDOM", true)]
    [InlineData("Other", false)]
    public void IsRandomIndustry_MatchesCaseInsensitive(string? input, bool expected)
    {
        var result = GenerationRequestNormalizer.IsRandomIndustry(input);

        Assert.Equal(expected, result);
    }
}
