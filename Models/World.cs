namespace EvidenceFoundry.Models;

public class World
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CaseContext CaseContext { get; set; } = new();
    public List<Organization> Plaintiffs { get; set; } = new();
    public List<Organization> Defendants { get; set; } = new();
    public List<Character> KeyPeople { get; set; } = new();
}
