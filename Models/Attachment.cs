namespace ReelDiscovery.Models;

public enum AttachmentType
{
    Word,
    Excel,
    PowerPoint,
    Image,
    CalendarInvite,
    Voicemail
}

public enum AttachmentComplexity
{
    Simple,
    Detailed
}

public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AttachmentType Type { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentDescription { get; set; } = string.Empty;
    public byte[]? Content { get; set; }

    /// <summary>
    /// For images: if true, the image should be displayed inline in the email body.
    /// If false, it's a regular attachment.
    /// </summary>
    public bool IsInline { get; set; }

    /// <summary>
    /// Content-ID for inline images (used in HTML img src="cid:xxx")
    /// </summary>
    public string? ContentId { get; set; }

    /// <summary>
    /// For attachment chains: tracks which document this is a version of
    /// </summary>
    public string? DocumentChainId { get; set; }

    /// <summary>
    /// For attachment chains: version number (e.g., "v1", "v2", "FINAL", "FINAL_v2")
    /// </summary>
    public string? VersionLabel { get; set; }

    /// <summary>
    /// For voicemails: the voice used for TTS generation
    /// </summary>
    public string? VoiceId { get; set; }

    public string MimeType => Type switch
    {
        AttachmentType.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        AttachmentType.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        AttachmentType.PowerPoint => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        AttachmentType.Image => "image/png",
        AttachmentType.CalendarInvite => "text/calendar",
        AttachmentType.Voicemail => "audio/mpeg",
        _ => "application/octet-stream"
    };

    public string Extension => Type switch
    {
        AttachmentType.Word => ".docx",
        AttachmentType.Excel => ".xlsx",
        AttachmentType.PowerPoint => ".pptx",
        AttachmentType.Image => ".png",
        AttachmentType.CalendarInvite => ".ics",
        AttachmentType.Voicemail => ".mp3",
        _ => ".bin"
    };
}
