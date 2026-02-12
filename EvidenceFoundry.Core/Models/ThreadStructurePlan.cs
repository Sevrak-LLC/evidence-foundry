namespace EvidenceFoundry.Models;

public sealed class ThreadStructurePlan
{
    private readonly List<ThreadEmailSlotPlan> _slots;
    private readonly Dictionary<Guid, ThreadEmailSlotPlan> _slotLookup;
    private readonly List<Guid> _chronologicalOrder;

    public ThreadStructurePlan(Guid threadId, Guid rootEmailId, IEnumerable<ThreadEmailSlotPlan> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        ThreadId = threadId;
        RootEmailId = rootEmailId;
        _slots = slots.OrderBy(s => s.Index).ToList();
        _slotLookup = _slots.ToDictionary(s => s.EmailId);
        _chronologicalOrder = _slots
            .OrderBy(s => s.SentDate)
            .ThenBy(s => s.Index)
            .Select(s => s.EmailId)
            .ToList();
    }

    public Guid ThreadId { get; }
    public Guid RootEmailId { get; }
    public IReadOnlyList<ThreadEmailSlotPlan> Slots => _slots;
    public IReadOnlyDictionary<Guid, ThreadEmailSlotPlan> SlotLookup => _slotLookup;
    public IReadOnlyList<Guid> ChronologicalOrder => _chronologicalOrder;
}

public enum ThreadEmailIntent
{
    New,
    Reply,
    Forward
}

public sealed record ThreadAttachmentPlan(
    bool HasDocument,
    AttachmentType? DocumentType,
    bool HasImage,
    bool IsImageInline,
    bool HasVoicemail);

public sealed record ThreadEmailSlotPlan(
    int Index,
    Guid EmailId,
    Guid? ParentEmailId,
    Guid RootEmailId,
    Guid BranchId,
    DateTime SentDate,
    string NarrativePhase,
    ThreadEmailIntent Intent,
    ThreadAttachmentPlan Attachments);
