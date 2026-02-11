namespace EvidenceFoundry.Models;

public class Organization
{
    private readonly List<Department> _departments = new();

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public OrganizationType OrganizationType { get; set; } = OrganizationType.Unknown;
    public Industry Industry { get; set; } = Industry.Other;
    public UsState State { get; set; } = UsState.Unknown;
    public DateTime? Founded { get; set; }
    public bool IsPlaintiff { get; set; }
    public bool IsDefendant { get; set; }
    public IReadOnlyList<Department> Departments => _departments;

    public void SetDepartments(IEnumerable<Department> departments)
    {
        ArgumentNullException.ThrowIfNull(departments);
        _departments.Clear();
        _departments.AddRange(departments);
    }

    public void AddDepartment(Department department) => _departments.Add(department);

    public void InsertDepartment(int index, Department department) => _departments.Insert(index, department);

    public void ClearDepartments() => _departments.Clear();

    public IEnumerable<(Character Character, Role Role, Department Department, Organization Organization)> EnumerateCharacters()
    {
        foreach (var department in Departments)
        {
            foreach (var role in department.Roles)
            {
                foreach (var character in role.Characters)
                {
                    yield return (character, role, department, this);
                }
            }
        }
    }

    public override string ToString() => Name;
}
