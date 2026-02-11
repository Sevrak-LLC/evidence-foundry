namespace EvidenceFoundry.Models;

public class Department
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DepartmentName Name { get; set; }
    public List<Role> Roles { get; set; } = new();
}
