namespace EvidenceFoundry.Models;

public class StoryOutline
{
    public string Point { get; set; } = string.Empty;
    public List<Organization> Organizations { get; set; } = new();
    public List<Character> Characters { get; set; } = new();
}
