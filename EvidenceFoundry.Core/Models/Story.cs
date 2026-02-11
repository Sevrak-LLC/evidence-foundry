namespace EvidenceFoundry.Models;

public class StoryBeat
{
    public Guid Id { get; set; }
    public Guid StorylineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Plot { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int EmailCount { get; set; }
    public List<EmailThread> Threads { get; set; } = new();
}
