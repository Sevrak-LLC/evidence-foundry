namespace EvidenceFoundry.Models;

public class EmailMessage
{
    public Guid Id { get; set; }
    public Guid EmailThreadId { get; set; }
    public Guid StoryBeatId { get; set; }
    public Guid StorylineId { get; set; }

    // Threading headers
    public string MessageId { get; set; } = string.Empty;
    public string? InReplyTo { get; set; }
    public List<string> References { get; set; } = new();

    // Addressing
    public Character From { get; set; } = null!;
    public List<Character> To { get; set; } = new();
    public List<Character> Cc { get; set; } = new();

    // Content
    public string Subject { get; set; } = string.Empty;
    public string BodyPlain { get; set; } = string.Empty;
    public string? BodyHtml { get; set; }
    public DateTime SentDate { get; set; }

    // Attachments
    public List<Attachment> Attachments { get; set; } = new();

    // Ordering within thread
    public int SequenceInThread { get; set; }

    // Generated filename for the .eml file
    public string? GeneratedFileName { get; set; }

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
}
