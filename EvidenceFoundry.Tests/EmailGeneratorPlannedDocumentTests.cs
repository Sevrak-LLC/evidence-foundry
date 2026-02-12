using System.Reflection;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorPlannedDocumentTests
{
    [Fact]
    public void TryResolvePlannedAttachmentTypeReturnsPlannedTypeWhenEnabled()
    {
        var config = new GenerationConfig
        {
            IncludeWord = false,
            IncludeExcel = true,
            IncludePowerPoint = false
        };

        var (result, attachmentType) = InvokeTryResolvePlannedAttachmentType("excel", config);

        Assert.True(result);
        Assert.Equal(AttachmentType.Excel, attachmentType);
    }

    [Fact]
    public void TryResolvePlannedAttachmentTypeFallsBackToFirstEnabledType()
    {
        var config = new GenerationConfig
        {
            IncludeWord = false,
            IncludeExcel = true,
            IncludePowerPoint = false
        };

        var (result, attachmentType) = InvokeTryResolvePlannedAttachmentType("word", config);

        Assert.True(result);
        Assert.Equal(AttachmentType.Excel, attachmentType);
    }

    [Fact]
    public void TryResolvePlannedAttachmentTypeReturnsFalseWhenNoneEnabled()
    {
        var config = new GenerationConfig
        {
            IncludeWord = false,
            IncludeExcel = false,
            IncludePowerPoint = false
        };

        var (result, _) = InvokeTryResolvePlannedAttachmentType("word", config);

        Assert.False(result);
    }

    private static (bool result, AttachmentType attachmentType) InvokeTryResolvePlannedAttachmentType(
        string plannedDocumentType,
        GenerationConfig config)
    {
        var method = typeof(EmailGenerator).GetMethod(
            "TryResolvePlannedAttachmentType",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var args = new object?[] { plannedDocumentType, config, null };
        var result = (bool)method.Invoke(null, args)!;
        var attachmentType = (AttachmentType)args[2]!;
        return (result, attachmentType);
    }
}
