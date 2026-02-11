namespace EvidenceFoundry.Models;

public class World
{
    private readonly List<Organization> _plaintiffs = new();
    private readonly List<Organization> _defendants = new();
    private readonly List<Character> _keyPeople = new();

    public Guid Id { get; set; }
    public CaseContext CaseContext { get; set; } = new();
    public IReadOnlyList<Organization> Plaintiffs => _plaintiffs;
    public IReadOnlyList<Organization> Defendants => _defendants;
    public IReadOnlyList<Character> KeyPeople => _keyPeople;

    public void SetPlaintiffs(IEnumerable<Organization> plaintiffs)
    {
        ArgumentNullException.ThrowIfNull(plaintiffs);
        _plaintiffs.Clear();
        _plaintiffs.AddRange(plaintiffs);
    }

    public void SetDefendants(IEnumerable<Organization> defendants)
    {
        ArgumentNullException.ThrowIfNull(defendants);
        _defendants.Clear();
        _defendants.AddRange(defendants);
    }

    public void SetKeyPeople(IEnumerable<Character> keyPeople)
    {
        ArgumentNullException.ThrowIfNull(keyPeople);
        _keyPeople.Clear();
        _keyPeople.AddRange(keyPeople);
    }

    public void ClearKeyPeople() => _keyPeople.Clear();

    public void AddKeyPerson(Character person) => _keyPeople.Add(person);

    public void AddKeyPeople(IEnumerable<Character> people)
    {
        ArgumentNullException.ThrowIfNull(people);
        _keyPeople.AddRange(people);
    }
}
