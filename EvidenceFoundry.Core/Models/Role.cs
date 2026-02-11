namespace EvidenceFoundry.Models;

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DepartmentId { get; set; }
    public Guid OrganizationId { get; set; }
    public RoleName Name { get; set; }
    public RoleName? ReportsToRole { get; set; }
    public List<Character> Characters { get; set; } = new();
}
