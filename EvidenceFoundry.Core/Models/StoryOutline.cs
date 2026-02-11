namespace EvidenceFoundry.Models;

public class StoryOutline
{
    private readonly List<Organization> _organizations = new();
    private readonly List<Character> _characters = new();

    public string Point { get; set; } = string.Empty;
    public IReadOnlyList<Organization> Organizations => _organizations;
    public IReadOnlyList<Character> Characters => _characters;

    public void SetOrganizations(IEnumerable<Organization> organizations)
    {
        ArgumentNullException.ThrowIfNull(organizations);
        _organizations.Clear();
        _organizations.AddRange(organizations);
    }

    public void SetCharacters(IEnumerable<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _characters.Clear();
        _characters.AddRange(characters);
    }
}
