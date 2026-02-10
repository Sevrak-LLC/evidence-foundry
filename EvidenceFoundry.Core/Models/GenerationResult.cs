namespace EvidenceFoundry.Models;

public class GenerationResult
{
    public int TotalEmailsGenerated { get; set; }
    public int TotalThreadsGenerated { get; set; }
    public int TotalAttachmentsGenerated { get; set; }
    public int WordDocumentsGenerated { get; set; }
    public int ExcelDocumentsGenerated { get; set; }
    public int PowerPointDocumentsGenerated { get; set; }
    public int ImagesGenerated { get; set; }
    public int CalendarInvitesGenerated { get; set; }
    public int VoicemailsGenerated { get; set; }
    public string OutputFolder { get; set; } = string.Empty;
    public TimeSpan ElapsedTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasCancelled { get; set; }
}

public class GenerationProgress
{
    public int TotalEmails { get; set; }
    public int CompletedEmails { get; set; }
    public int TotalAttachments { get; set; }
    public int CompletedAttachments { get; set; }
    public int TotalImages { get; set; }
    public int CompletedImages { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
    public string? CurrentStoryline { get; set; }

    public double OverallPercentage =>
        TotalEmails == 0 ? 0 : (CompletedEmails * 100.0) / TotalEmails;

    public GenerationProgress Snapshot()
    {
        return new GenerationProgress
        {
            TotalEmails = TotalEmails,
            CompletedEmails = CompletedEmails,
            TotalAttachments = TotalAttachments,
            CompletedAttachments = CompletedAttachments,
            TotalImages = TotalImages,
            CompletedImages = CompletedImages,
            CurrentOperation = CurrentOperation,
            CurrentStoryline = CurrentStoryline
        };
    }
}
