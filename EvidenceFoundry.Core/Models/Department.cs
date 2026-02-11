namespace EvidenceFoundry.Models;

public class Department
{
    private readonly List<Role> _roles = new();

    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public DepartmentName Name { get; set; }
    public IReadOnlyList<Role> Roles => _roles;

    public void SetRoles(IEnumerable<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles.Clear();
        _roles.AddRange(roles);
    }

    public void AddRole(Role role) => _roles.Add(role);

    public void ClearRoles() => _roles.Clear();

    public bool RemoveRole(Role role) => _roles.Remove(role);
}
