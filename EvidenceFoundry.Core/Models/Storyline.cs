namespace EvidenceFoundry.Models;

public class Storyline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Logline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<StoryOutline> PlotOutline { get; set; } = new();
    public List<string> TensionDrivers { get; set; } = new();
    public List<string> Ambiguities { get; set; } = new();
    public List<string> RedHerrings { get; set; } = new();
    public List<string> EvidenceThemes { get; set; } = new();
    public List<StoryBeat> Beats { get; set; } = new();
    public List<Organization> Organizations { get; set; } = new();
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int EmailCount => Beats?.Sum(b => b.EmailCount) ?? 0;
    public int ThreadCount => Beats?.Sum(b => b.Threads?.Count ?? 0) ?? 0;

    public override string ToString() => Title;
}
