using System.Globalization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

internal static class ThreadStructurePlanner
{
    public static ThreadStructurePlan BuildPlan(
        EmailThread thread,
        int emailCount,
        DateTime start,
        DateTime end,
        GenerationConfig config,
        int generationSeed)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(config);
        if (emailCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(emailCount), "Thread email count must be positive.");

        var rng = new Random(DeterministicSeedHelper.CreateSeed(
            "thread-plan",
            generationSeed.ToString(CultureInfo.InvariantCulture),
            thread.Id.ToString("N")));

        var dates = DateHelper.DistributeDatesForThread(emailCount, start, end, rng);
        if (dates.Count < emailCount)
        {
            while (dates.Count < emailCount)
            {
                dates.Add(end);
            }
        }

        var parentIndices = BuildParentPlan(emailCount, rng);
        var attachmentAssignments = BuildAttachmentAssignments(emailCount, config, rng);

        var emailIds = thread.EmailMessages.Count == emailCount
            ? thread.EmailMessages.Select(m => m.Id).ToList()
            : Enumerable.Range(0, emailCount)
                .Select(i => DeterministicIdHelper.CreateGuid(
                    "email-message",
                    thread.Id.ToString("N"),
                    i.ToString(CultureInfo.InvariantCulture)))
                .ToList();

        var rootEmailId = emailIds[0];
        var rootBranchId = DeterministicIdHelper.CreateGuid(
            "email-branch",
            thread.Id.ToString("N"),
            "root");

        var slots = new List<ThreadEmailSlotPlan>(emailCount);
        var branchIds = new Guid[emailCount];
        branchIds[0] = rootBranchId;

        for (var i = 0; i < emailCount; i++)
        {
            var parentIndex = parentIndices[i];
            var parentEmailId = parentIndex >= 0 ? emailIds[parentIndex] : (Guid?)null;

            if (i > 0)
            {
                if (parentIndex == i - 1)
                {
                    branchIds[i] = branchIds[parentIndex];
                }
                else
                {
                    branchIds[i] = DeterministicIdHelper.CreateGuid(
                        "email-branch",
                        thread.Id.ToString("N"),
                        parentIndex.ToString(CultureInfo.InvariantCulture),
                        i.ToString(CultureInfo.InvariantCulture));
                }
            }

            var intent = ResolveIntent(i, parentIndex, rng);
            var attachments = BuildAttachmentPlanForSlot(i, attachmentAssignments, config, rng);

            slots.Add(new ThreadEmailSlotPlan(
                i,
                emailIds[i],
                parentEmailId,
                rootEmailId,
                branchIds[i],
                dates[i],
                ResolveNarrativePhase(i, emailCount),
                intent,
                attachments));
        }

        return new ThreadStructurePlan(thread.Id, rootEmailId, slots);
    }

    private static int[] BuildParentPlan(int emailCount, Random rng)
    {
        var parents = new int[emailCount];
        parents[0] = -1;
        for (var i = 1; i < emailCount; i++)
        {
            parents[i] = i - 1;
        }

        var branchCount = ResolveBranchCount(emailCount, rng);
        if (branchCount == 0)
            return parents;

        var usedChildren = new HashSet<int>();
        for (var i = 0; i < branchCount; i++)
        {
            if (emailCount < 3)
                break;

            var childIndex = rng.Next(2, emailCount);
            if (!usedChildren.Add(childIndex))
                continue;

            var parentIndex = rng.Next(0, childIndex - 1);
            if (parentIndex == childIndex - 1)
            {
                parentIndex = Math.Max(0, parentIndex - 1);
            }

            parents[childIndex] = parentIndex;
        }

        return parents;
    }

    private static int ResolveBranchCount(int emailCount, Random rng)
    {
        if (emailCount < 5)
            return 0;

        if (emailCount < 8)
            return rng.NextDouble() < 0.6 ? 1 : 0;

        if (emailCount < 12)
            return rng.NextDouble() < 0.7 ? 1 : 2;

        return rng.NextDouble() < 0.4 ? 1 : 2;
    }

    private static ThreadEmailIntent ResolveIntent(int index, int parentIndex, Random rng)
    {
        if (index == 0 || parentIndex < 0)
            return ThreadEmailIntent.New;

        if (parentIndex != index - 1)
            return rng.NextDouble() < 0.45 ? ThreadEmailIntent.Forward : ThreadEmailIntent.Reply;

        return rng.NextDouble() < 0.12 ? ThreadEmailIntent.Forward : ThreadEmailIntent.Reply;
    }

    private sealed record AttachmentAssignments(
        HashSet<int> DocSlots,
        Dictionary<int, AttachmentType> DocTypes,
        HashSet<int> ImageSlots,
        HashSet<int> InlineImages,
        HashSet<int> VoicemailSlots);

    private static AttachmentAssignments BuildAttachmentAssignments(
        int emailCount,
        GenerationConfig config,
        Random rng)
    {
        var (docs, images, voicemails) = EmailGenerator.CalculateAttachmentTotals(config, emailCount);
        docs = Math.Min(docs, emailCount);
        images = Math.Min(images, emailCount);
        voicemails = Math.Min(voicemails, emailCount);

        var docSlots = PickAttachmentSlots(emailCount, docs, rng);
        var imageSlots = config.IncludeImages ? PickAttachmentSlots(emailCount, images, rng) : new HashSet<int>();
        var voicemailSlots = config.IncludeVoicemails ? PickAttachmentSlots(emailCount, voicemails, rng) : new HashSet<int>();

        var docTypes = new Dictionary<int, AttachmentType>();
        if (docSlots.Count > 0 && config.EnabledAttachmentTypes.Count > 0)
        {
            foreach (var slot in docSlots)
            {
                var type = config.EnabledAttachmentTypes[rng.Next(config.EnabledAttachmentTypes.Count)];
                docTypes[slot] = type;
            }
        }

        var inlineImages = new HashSet<int>();
        foreach (var slot in imageSlots)
        {
            if (rng.NextDouble() < 0.7)
                inlineImages.Add(slot);
        }

        return new AttachmentAssignments(docSlots, docTypes, imageSlots, inlineImages, voicemailSlots);
    }

    private static HashSet<int> PickAttachmentSlots(int emailCount, int totalAttachments, Random rng)
    {
        var slots = new HashSet<int>();
        if (totalAttachments <= 0)
            return slots;

        totalAttachments = Math.Min(totalAttachments, emailCount);
        var remaining = totalAttachments;
        var boost = 0.0;

        for (var i = 0; i < emailCount; i++)
        {
            var slotsLeft = emailCount - i;
            if (remaining <= 0)
                break;

            if (slotsLeft == remaining)
            {
                slots.Add(i);
                remaining--;
                continue;
            }

            var baseProb = i switch
            {
                0 => 0.75,
                1 => 0.35,
                _ => 0.18 + (0.6 * (double)i / Math.Max(1, emailCount - 1))
            };

            var probability = Math.Min(0.95, baseProb + boost);
            if (rng.NextDouble() < probability)
            {
                slots.Add(i);
                remaining--;
                boost = 0.0;
            }
            else
            {
                boost = Math.Min(0.45, boost + 0.15);
            }
        }

        for (var i = emailCount - 1; remaining > 0 && i >= 0; i--)
        {
            if (slots.Add(i))
                remaining--;
        }

        return slots;
    }

    private static ThreadAttachmentPlan BuildAttachmentPlanForSlot(
        int index,
        AttachmentAssignments assignments,
        GenerationConfig config,
        Random rng)
    {
        var hasDoc = assignments.DocSlots.Contains(index);
        assignments.DocTypes.TryGetValue(index, out var docType);

        var hasImage = assignments.ImageSlots.Contains(index);
        var isInline = hasImage && assignments.InlineImages.Contains(index);

        var hasVoicemail = assignments.VoicemailSlots.Contains(index);

        return new ThreadAttachmentPlan(
            hasDoc,
            hasDoc ? docType : null,
            config.IncludeImages && hasImage,
            isInline,
            config.IncludeVoicemails && hasVoicemail);
    }

    private static string ResolveNarrativePhase(int index, int total)
    {
        if (total <= 1)
            return "SINGLE - Introduce the conflict and leave open questions.";

        var fraction = (double)index / Math.Max(1, total - 1);
        if (fraction < 0.34)
            return "BEGINNING - Set up the conflict and stakes.";
        if (fraction > 0.66)
            return "LATE - Escalate consequences without full resolution.";

        return "MIDDLE - Escalate tension and develop the conflict.";
    }
}
