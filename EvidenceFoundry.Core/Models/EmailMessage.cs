namespace EvidenceFoundry.Models;

public class EmailMessage
{
    private readonly List<string> _references = new();
    private readonly List<Character> _to = new();
    private readonly List<Character> _cc = new();
    private readonly List<Attachment> _attachments = new();

    public Guid Id { get; set; }
    public Guid EmailThreadId { get; set; }
    public Guid StoryBeatId { get; set; }
    public Guid StorylineId { get; set; }
    public Guid? ParentEmailId { get; set; }
    public Guid RootEmailId { get; set; }
    public Guid BranchId { get; set; }

    // Threading headers
    public string MessageId { get; set; } = string.Empty;
    public string? InReplyTo { get; set; }
    public IReadOnlyList<string> References => _references;

    // Addressing
    public Character From { get; set; } = null!;
    public IReadOnlyList<Character> To => _to;
    public IReadOnlyList<Character> Cc => _cc;

    // Content
    public string Subject { get; set; } = string.Empty;
    public string BodyPlain { get; set; } = string.Empty;
    public string? BodyHtml { get; set; }
    public DateTime SentDate { get; set; }

    // Attachments
    public IReadOnlyList<Attachment> Attachments => _attachments;

    // Ordering within thread
    public int SequenceInThread { get; set; }

    // Generated filename for the .eml file
    public string? GeneratedFileName { get; set; }

    // Generation status
    public bool GenerationFailed { get; set; }
    public string? GenerationFailureReason { get; set; }

    // Attachment planning - these are set by AI during email generation
    // and used later to generate the actual attachments
    public bool PlannedHasDocument { get; set; }
    public string? PlannedDocumentType { get; set; } // "word", "excel", "powerpoint"
    public string? PlannedDocumentDescription { get; set; }
    public bool PlannedHasImage { get; set; }
    public string? PlannedImageDescription { get; set; }
    public bool PlannedIsImageInline { get; set; }
    public bool PlannedHasVoicemail { get; set; }
    public string? PlannedVoicemailContext { get; set; }

    public void SetReferences(IEnumerable<string> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        _references.Clear();
        _references.AddRange(references);
    }

    public void SetTo(IEnumerable<Character> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        _to.Clear();
        _to.AddRange(recipients);
    }

    public void SetCc(IEnumerable<Character> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        _cc.Clear();
        _cc.AddRange(recipients);
    }

    public void SetAttachments(IEnumerable<Attachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments.Clear();
        _attachments.AddRange(attachments);
    }

    public void AddAttachment(Attachment attachment) => _attachments.Add(attachment);
}
