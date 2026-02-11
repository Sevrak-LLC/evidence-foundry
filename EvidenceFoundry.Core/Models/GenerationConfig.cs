using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Models;

public class GenerationConfig
{
    public GenerationConfig()
    {
        var now = Clock.LocalNowDateTime;
        StartDate = now.AddMonths(-3);
        EndDate = now;
    }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool LetAISuggestDates { get; set; } = true;
    public int AttachmentPercentage { get; set; } = 20;
    public AttachmentComplexity AttachmentComplexity { get; set; } = AttachmentComplexity.Detailed;
    public string OutputFolder { get; set; } = string.Empty;
    public bool IncludeWord { get; set; } = true;
    public bool IncludeExcel { get; set; } = true;
    public bool IncludePowerPoint { get; set; } = true;
    public int ParallelThreads { get; set; } = 3; // Number of concurrent API calls for storyline generation
    public bool OrganizeBySender { get; set; } = true; // Create subfolders for each sender email address

    // Image generation settings
    public bool IncludeImages { get; set; } = false;
    public int ImagePercentage { get; set; } = 10; // Percentage of emails that get images (separate from document attachments)

    // Calendar invite settings
    public bool IncludeCalendarInvites { get; set; } = true; // Auto-detect meetings in emails and attach .ics files
    public int CalendarInvitePercentage { get; set; } = 10; // Max percentage of emails to check for meeting detection

    // Attachment chain settings (document versioning)
    public bool EnableAttachmentChains { get; set; } = true; // Documents evolve across threads with versions

    // Voicemail settings (TTS)
    public bool IncludeVoicemails { get; set; } = false;
    public int VoicemailPercentage { get; set; } = 5; // Percentage of emails that get voicemail attachments

    public List<AttachmentType> EnabledAttachmentTypes
    {
        get
        {
            var types = new List<AttachmentType>();
            if (IncludeWord) types.Add(AttachmentType.Word);
            if (IncludeExcel) types.Add(AttachmentType.Excel);
            if (IncludePowerPoint) types.Add(AttachmentType.PowerPoint);
            // Note: Images are handled separately, not in this list
            return types;
        }
    }
}
