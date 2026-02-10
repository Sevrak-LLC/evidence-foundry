using System.Reflection;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorAttachmentTotalsTests
{
    [Fact]
    public void CalculateAttachmentTotals_UsesEnabledAttachmentTypesAndPercentages()
    {
        var config = new GenerationConfig
        {
            AttachmentPercentage = 20,
            IncludeWord = true,
            IncludeExcel = false,
            IncludePowerPoint = false,
            IncludeImages = true,
            ImagePercentage = 10,
            IncludeVoicemails = false
        };

        var totals = InvokeCalculateAttachmentTotals(config, 10);

        Assert.Equal(2, totals.totalDocAttachments);
        Assert.Equal(1, totals.totalImageAttachments);
        Assert.Equal(0, totals.totalVoicemailAttachments);
    }

    [Fact]
    public void CalculateAttachmentTotals_IgnoresAttachmentsWhenNoneEnabled()
    {
        var config = new GenerationConfig
        {
            AttachmentPercentage = 50,
            IncludeWord = false,
            IncludeExcel = false,
            IncludePowerPoint = false,
            IncludeImages = false,
            IncludeVoicemails = false
        };

        var totals = InvokeCalculateAttachmentTotals(config, 10);

        Assert.Equal(0, totals.totalDocAttachments);
        Assert.Equal(0, totals.totalImageAttachments);
        Assert.Equal(0, totals.totalVoicemailAttachments);
    }

    private static (int totalDocAttachments, int totalImageAttachments, int totalVoicemailAttachments) InvokeCalculateAttachmentTotals(
        GenerationConfig config,
        int emailCount)
    {
        var method = typeof(EmailGenerator).GetMethod(
            "CalculateAttachmentTotals",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var result = (ValueTuple<int, int, int>)method.Invoke(null, new object[] { config, emailCount })!;
        return (result.Item1, result.Item2, result.Item3);
    }
}
