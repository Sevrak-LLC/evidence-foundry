using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class ThreadStructurePlannerTests
{
    [Fact]
    public void BuildPlanIsDeterministicForSeed()
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Forecast"
        };

        EmailThreadGenerator.EnsurePlaceholderMessages(thread, 5);

        var config = new GenerationConfig
        {
            AttachmentPercentage = 40,
            IncludeWord = true,
            IncludeExcel = true,
            IncludePowerPoint = false,
            IncludeImages = true,
            ImagePercentage = 20,
            IncludeVoicemails = true,
            VoicemailPercentage = 10
        };

        var start = new DateTime(2024, 2, 1);
        var end = new DateTime(2024, 2, 5);

        var plan1 = ThreadStructurePlanner.BuildPlan(thread, 5, start, end, config, generationSeed: 777);
        var plan2 = ThreadStructurePlanner.BuildPlan(thread, 5, start, end, config, generationSeed: 777);

        Assert.Equal(SerializePlan(plan1), SerializePlan(plan2));
    }

    [Fact]
    public void BuildPlanUsesValidParentsAndAttachmentCounts()
    {
        var thread = new EmailThread
        {
            StoryBeatId = Guid.NewGuid(),
            StorylineId = Guid.NewGuid(),
            Topic = "Audit"
        };

        EmailThreadGenerator.EnsurePlaceholderMessages(thread, 6);

        var config = new GenerationConfig
        {
            AttachmentPercentage = 50,
            IncludeWord = true,
            IncludeExcel = true,
            IncludePowerPoint = true,
            IncludeImages = true,
            ImagePercentage = 25,
            IncludeVoicemails = true,
            VoicemailPercentage = 20
        };

        var plan = ThreadStructurePlanner.BuildPlan(
            thread,
            6,
            new DateTime(2024, 4, 1),
            new DateTime(2024, 4, 5),
            config,
            generationSeed: 1234);

        Assert.Null(plan.Slots[0].ParentEmailId);
        foreach (var slot in plan.Slots.Skip(1))
        {
            Assert.NotNull(slot.ParentEmailId);
            var parentIndex = plan.Slots.Select((s, i) => new { s.EmailId, Index = i })
                .FirstOrDefault(x => x.EmailId == slot.ParentEmailId)?.Index ?? -1;
            Assert.True(parentIndex >= 0);
            Assert.True(parentIndex < slot.Index);
        }

        var totals = EmailGenerator.CalculateAttachmentTotals(config, plan.Slots.Count);
        var docCount = plan.Slots.Count(s => s.Attachments.HasDocument);
        var imageCount = plan.Slots.Count(s => s.Attachments.HasImage);
        var voicemailCount = plan.Slots.Count(s => s.Attachments.HasVoicemail);

        Assert.Equal(totals.totalDocAttachments, docCount);
        Assert.Equal(totals.totalImageAttachments, imageCount);
        Assert.Equal(totals.totalVoicemailAttachments, voicemailCount);
    }

    private static string SerializePlan(ThreadStructurePlan plan)
    {
        return string.Join("|", plan.Slots.Select(s =>
            $"{s.Index}:{s.ParentEmailId}:{s.SentDate:O}:{s.Intent}:{s.Attachments.HasDocument}:{s.Attachments.DocumentType}:{s.Attachments.HasImage}:{s.Attachments.HasVoicemail}"));
    }
}
