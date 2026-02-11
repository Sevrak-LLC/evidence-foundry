using System.Text.Json;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

public static class PromptPayloadSerializer
{
    public static string SerializePayload<T>(T payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        return JsonSerializer.Serialize(payload, JsonSerializationDefaults.Indented);
    }

    public static string SerializeOrganization(Organization organization, bool includeCharacters = false)
    {
        ArgumentNullException.ThrowIfNull(organization);

        return SerializePayload(BuildOrganizationPayload(organization, includeCharacters));
    }

    public static string SerializeOrganizations(IEnumerable<Organization> organizations, bool includeCharacters = false)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        var payload = organizations.Select(org => BuildOrganizationPayload(org, includeCharacters));
        return SerializePayload(payload);
    }

    public static string SerializeCharacters(Organization organization, bool humanizeRoleDepartment = false)
    {
        ArgumentNullException.ThrowIfNull(organization);

        return SerializeCharacters(new[] { organization }, humanizeRoleDepartment);
    }

    public static string SerializeCharacters(
        IEnumerable<Organization> organizations,
        bool humanizeRoleDepartment = false)
    {
        ArgumentNullException.ThrowIfNull(organizations);

        var payload = organizations
            .SelectMany(org => org.EnumerateCharacters().Select(a => new
            {
                firstName = a.Character.FirstName,
                lastName = a.Character.LastName,
                email = a.Character.Email,
                role = humanizeRoleDepartment
                    ? EnumHelper.HumanizeEnumName(a.Role.Name.ToString())
                    : a.Role.Name.ToString(),
                department = humanizeRoleDepartment
                    ? EnumHelper.HumanizeEnumName(a.Department.Name.ToString())
                    : a.Department.Name.ToString(),
                organization = org.Name
            }))
            .ToList();

        return SerializePayload(payload);
    }

    private static object BuildOrganizationPayload(Organization organization, bool includeCharacters)
    {
        return new
        {
            name = organization.Name,
            domain = organization.Domain,
            description = organization.Description,
            organizationType = organization.OrganizationType.ToString(),
            industry = organization.Industry.ToString(),
            state = organization.State.ToString(),
            plaintiff = organization.IsPlaintiff,
            defendant = organization.IsDefendant,
            founded = organization.Founded?.ToString("yyyy-MM-dd"),
            departments = organization.Departments.Select(d => new
            {
                name = d.Name.ToString(),
                roles = d.Roles.Select(r => new
                {
                    name = r.Name.ToString(),
                    reportsToRole = r.ReportsToRole?.ToString(),
                    characters = includeCharacters
                        ? r.Characters.Select(c => new { firstName = c.FirstName, lastName = c.LastName, email = c.Email })
                        : null
                })
            })
        };
    }
}
