namespace EvidenceFoundry.Models;

public class Role
{
    private readonly List<Character> _characters = new();

    public Guid Id { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public RoleName Name { get; set; }
    public RoleName? ReportsToRole { get; set; }
    public IReadOnlyList<Character> Characters => _characters;

    public void SetCharacters(IEnumerable<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _characters.Clear();
        _characters.AddRange(characters);
    }

    public void AddCharacter(Character character) => _characters.Add(character);

    public void AddCharacters(IEnumerable<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        _characters.AddRange(characters);
    }

    public bool RemoveCharacter(Character character) => _characters.Remove(character);

    public void ClearCharacters() => _characters.Clear();
}
