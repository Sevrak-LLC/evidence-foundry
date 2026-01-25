namespace ReelDiscovery.Models;

public class Storyline
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Guid> InvolvedCharacterIds { get; set; } = new();
    public string TimelineHint { get; set; } = string.Empty;
    public int SuggestedEmailCount { get; set; }

    public override string ToString() => Title;
}
