using System.Globalization;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class OrganizationGenerator
{
    private readonly OpenAIService _openAI;

    private static readonly OrganizationType[] OrganizationTypes = Enum.GetValues<OrganizationType>()
        .Where(t => t != OrganizationType.Unknown)
        .ToArray();

    private static readonly UsState[] UsStates = Enum.GetValues<UsState>()
        .Where(s => s != UsState.Unknown)
        .ToArray();


    public OrganizationGenerator(OpenAIService openAI)
    {
        _openAI = openAI;
    }

    public async Task<List<Organization>> GenerateKnownOrganizationsAsync(
        string topic,
        Storyline storyline,
        CancellationToken ct)
    {
        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Organization Extractor.
Extract ONLY organizations that are explicitly mentioned or clearly implied in the storyline description.
Do NOT invent characters. Do NOT add speculative details.

Rules:
- Entities must be type ""Organization"".
- If an org is plaintiff or defendant, set that boolean; otherwise set both false.
- plaintiff and defendant cannot both be true.
- Include departments/roles ONLY if they are explicitly stated in the storyline.
- Use only the provided enum values for department/role names.
 - If an industry can be reasonably inferred from the description, include it; otherwise use ""Other"".");

        var departments = EnumHelper.FormatEnumOptions<DepartmentName>();
        var roles = EnumHelper.FormatEnumOptions<RoleName>();
        var orgTypes = EnumHelper.FormatEnumOptions<OrganizationType>();
        var industries = EnumHelper.FormatEnumOptions<Industry>();
        var states = EnumHelper.FormatEnumOptions<UsState>();

        var schema = """
{
  "entities": [
    {
      "type": "Organization",
      "name": "string",
      "domain": "string (only if explicitly mentioned, otherwise empty)",
      "description": "string (only if explicitly stated, otherwise empty)",
      "organizationType": "OrganizationType enum value (only if explicitly stated, otherwise empty)",
      "industry": "Industry enum value (only if explicitly stated or reasonably inferred, otherwise Other)",
      "state": "UsState enum value (only if explicitly stated, otherwise empty)",
      "plaintiff": true|false,
      "defendant": true|false,
      "departments": [
        {
          "name": "DepartmentName enum value",
          "roles": [
            {
              "name": "RoleName enum value",
              "reportsToRole": "RoleName enum value or null"
            }
          ]
        }
      ]
    }
  ]
}
""";

        var userPrompt = PromptScaffolding.JoinSections($@"Topic: {topic}

Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}

Allowed department names (use exact values only):
{departments}

Allowed role names (use exact values only):
{roles}

Allowed organization types (use exact values only):
{orgTypes}

Allowed industries (use exact values only; use Other if unknown):
{industries}

Allowed US states (use exact values only):
{states}

Enum values are shown as Raw (Humanized). Return only the Raw enum value.
", PromptScaffolding.JsonSchemaSection(schema));

        var response = await _openAI.GetJsonCompletionAsync<OrganizationSeedResponse>(
            systemPrompt,
            userPrompt,
            "Organization Extraction",
            ct);

        if (response?.Organizations == null)
            return new List<Organization>();

        return ParseSeedOrganizations(response);
    }

    internal static List<Organization> ParseSeedOrganizations(OrganizationSeedResponse response)
    {
        var organizations = new List<Organization>();

        foreach (var entity in response.Organizations)
        {
            if (!TryBuildSeedOrganization(entity, out var org))
                continue;

            organizations.Add(org);
        }

        return organizations;
    }

    public async Task<Organization> EnrichOrganizationAsync(
        Storyline storyline,
        Organization seed,
        CancellationToken ct)
    {
        var systemPrompt = PromptScaffolding.AppendJsonOnlyInstruction(@"You are the EvidenceFoundry Organization Builder.
Fill in missing organization details and build out a realistic department/role structure.

Rules:
- Use ONLY the provided department/role enum values.
- Keep existing departments/roles; add missing ones as appropriate.
- Departments must align with the industry and organization type guidance.
- Roles must align with the allowed roles for each department.
- Do NOT include characters.
- Founded date must be at least 1 year prior to the storyline start date.
- OrganizationType, Industry, and State must be provided using enum values.
 - plaintiff and defendant cannot both be true.");

        var allowedDepartmentsJson = DepartmentGenerator.BuildAllowedDepartmentsJson(seed.Industry, seed.OrganizationType);
        var allowedDepartmentRoleMapJson = DepartmentGenerator.BuildAllowedDepartmentRoleMapJson(seed.Industry, seed.OrganizationType);
        var includeScopedMaps = seed.Industry != Industry.Other || seed.OrganizationType != OrganizationType.Unknown;
        var departments = EnumHelper.FormatEnumOptions<DepartmentName>();
        var roles = EnumHelper.FormatEnumOptions<RoleName>();
        var orgTypes = EnumHelper.FormatEnumOptions<OrganizationType>();
        var industries = EnumHelper.FormatEnumOptions<Industry>();
        var states = EnumHelper.FormatEnumOptions<UsState>();
        var orgJson = PromptPayloadSerializer.SerializeOrganization(seed);

        var userPrompt = BuildOrganizationPrompt(
            storyline,
            new OrganizationPromptOptions(
                orgJson,
                departments,
                roles,
                includeScopedMaps,
                allowedDepartmentsJson,
                allowedDepartmentRoleMapJson,
                orgTypes,
                industries,
                states));

        var response = await _openAI.GetJsonCompletionAsync<OrganizationDto>(
            systemPrompt,
            userPrompt,
            $"Organization Build: {seed.Name}",
            ct);

        return BuildOrganizationFromResponse(seed, response);
    }

    private static string BuildOrganizationPrompt(
        Storyline storyline,
        OrganizationPromptOptions options)
    {
        var schema = """
{
  "name": "string",
  "domain": "string",
  "description": "string",
  "organizationType": "OrganizationType enum value",
  "industry": "Industry enum value",
  "state": "UsState enum value",
  "founded": "YYYY-MM-DD",
  "plaintiff": true|false,
  "defendant": true|false,
  "departments": [
    {
      "name": "DepartmentName enum value",
      "roles": [
        {
          "name": "RoleName enum value",
          "reportsToRole": "RoleName enum value or null"
        }
      ]
    }
  ]
}
""";

        return PromptScaffolding.JoinSections($@"Storyline title: {storyline.Title}
Storyline summary:
{storyline.Summary}
Storyline start date: {storyline.StartDate:yyyy-MM-dd}

Known organization data (JSON):
{options.OrgJson}

Allowed department names (use exact values only):
{options.Departments}

Allowed role names (use exact values only):
{options.Roles}

{(options.IncludeScopedMaps
    ? $@"Allowed departments for this organization (based on current industry/org type):
{options.AllowedDepartmentsJson}

Allowed roles for those departments:
{options.AllowedDepartmentRoleMapJson}
"
    : string.Empty)}

Allowed organization types (use exact values only):
{options.OrgTypes}

Allowed industries (use exact values only):
{options.Industries}

Allowed US states (use exact values only):
{options.States}

Enum values are shown as Raw (Humanized). Return only the Raw enum value.
", PromptScaffolding.JsonSchemaSection(schema));
    }

    private sealed record OrganizationPromptOptions(
        string OrgJson,
        string Departments,
        string Roles,
        bool IncludeScopedMaps,
        string AllowedDepartmentsJson,
        string AllowedDepartmentRoleMapJson,
        string OrgTypes,
        string Industries,
        string States);

    private static Organization BuildOrganizationFromResponse(Organization seed, OrganizationDto? response)
    {
        if (response == null)
            throw new InvalidOperationException($"Failed to build organization details for '{seed.Name}'.");

        if (response.Plaintiff && response.Defendant)
            throw new InvalidOperationException($"Organization '{response.Name}' cannot be both plaintiff and defendant.");

        var organizationId = seed.Id == Guid.Empty
            ? DeterministicIdHelper.CreateGuid("organization", seed.Name)
            : seed.Id;

        var organization = new Organization
        {
            Id = organizationId,
            Name = string.IsNullOrWhiteSpace(response.Name) ? seed.Name : response.Name.Trim(),
            Domain = response.Domain?.Trim() ?? seed.Domain,
            Description = response.Description?.Trim() ?? seed.Description,
            OrganizationType = EnumHelper.TryParseEnum(response.OrganizationType, out OrganizationType orgType)
                ? orgType
                : seed.OrganizationType,
            Industry = EnumHelper.TryParseEnum(response.Industry, out Industry industry)
                ? industry
                : seed.Industry,
            State = EnumHelper.TryParseEnum(response.State, out UsState state)
                ? state
                : seed.State,
            IsPlaintiff = response.Plaintiff,
            IsDefendant = response.Defendant
        };

        if (DateTime.TryParse(response.Founded, CultureInfo.InvariantCulture, DateTimeStyles.None, out var founded))
        {
            organization.Founded = founded;
        }

        PopulateDepartments(organization.Departments, response.Departments);

        return organization;
    }

    private static bool TryBuildSeedOrganization(OrganizationSeedDto entity, out Organization organization)
    {
        organization = default!;
        if (!string.Equals(entity.Type, "Organization", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(entity.Name))
            return false;
        if (entity.Plaintiff && entity.Defendant)
            throw new InvalidOperationException($"Organization '{entity.Name}' cannot be both plaintiff and defendant.");

        organization = new Organization
        {
            Id = DeterministicIdHelper.CreateGuid("organization", entity.Name.Trim()),
            Name = entity.Name.Trim(),
            Domain = entity.Domain?.Trim() ?? string.Empty,
            Description = entity.Description?.Trim() ?? string.Empty,
            OrganizationType = EnumHelper.TryParseEnum(entity.OrganizationType, out OrganizationType orgType)
                ? orgType
                : OrganizationType.Unknown,
            Industry = EnumHelper.TryParseEnum(entity.Industry, out Industry industry)
                ? industry
                : Industry.Other,
            State = EnumHelper.TryParseEnum(entity.State, out UsState state)
                ? state
                : UsState.Unknown,
            IsPlaintiff = entity.Plaintiff,
            IsDefendant = entity.Defendant
        };

        PopulateDepartments(organization.Departments, entity.Departments);
        return true;
    }

    private static void PopulateDepartments(
        List<Department> target,
        IEnumerable<DepartmentDto>? departments)
    {
        if (departments == null)
            return;

        foreach (var deptDto in departments)
        {
            if (!EnumHelper.TryParseEnum(deptDto.Name, out DepartmentName deptName))
                continue;

            var department = new Department { Name = deptName };
            PopulateRoles(department.Roles, deptDto.Roles);
            target.Add(department);
        }
    }

    private static void PopulateRoles(
        List<Role> target,
        IEnumerable<RoleDto>? roles)
    {
        if (roles == null)
            return;

        foreach (var roleDto in roles)
        {
            if (!EnumHelper.TryParseEnum(roleDto.Name, out RoleName roleName))
                continue;

            var role = new Role
            {
                Name = roleName,
                ReportsToRole = EnumHelper.TryParseEnum(roleDto.ReportsToRole, out RoleName reportsTo)
                    ? reportsTo
                    : null
            };
            target.Add(role);
        }
    }

    internal static void NormalizeOrganization(Organization organization, DateTime storylineStartDate, HashSet<string> usedDomains)
    {
        if (string.IsNullOrWhiteSpace(organization.Name))
            throw new InvalidOperationException("Organization name is required.");

        if (organization.IsPlaintiff && organization.IsDefendant)
            throw new InvalidOperationException($"Organization '{organization.Name}' cannot be both plaintiff and defendant.");

        organization.Id = organization.Id == Guid.Empty
            ? DeterministicIdHelper.CreateGuid("organization", organization.Name)
            : organization.Id;

        organization.Domain = NormalizeDomain(organization.Domain, organization.Name, usedDomains);
        organization.Founded = DateHelper.NormalizeFoundedDate(organization.Founded, storylineStartDate);
        organization.OrganizationType = NormalizeOrganizationType(organization.OrganizationType, organization.Name);
        organization.State = NormalizeState(organization.State, organization.Name);
        DepartmentGenerator.ApplyDepartmentRoleConstraints(organization);

        if (organization.Departments.Count == 0)
        {
            organization.Departments.Add(new Department
            {
                Name = DepartmentName.Executive,
                Roles = new List<Role> { new() { Name = RoleName.ChiefExecutiveOfficer } }
            });
        }

        RoleGenerator.EnsureSingleOccupantRolesInExecutive(organization);

        foreach (var department in organization.Departments)
        {
            if (department.Id == Guid.Empty)
            {
                department.Id = DeterministicIdHelper.CreateGuid(
                    "department",
                    organization.Id.ToString("N"),
                    department.Name.ToString());
            }
            if (department.Roles.Count == 0)
            {
                var defaults = DepartmentGenerator.GetAllowedRoles(organization.Industry, organization.OrganizationType, department.Name);
                if (defaults.Count > 0)
                    department.Roles.Add(new Role { Name = defaults[0] });
            }

            department.Roles = department.Roles
                .GroupBy(r => r.Name)
                .Select(g => g.First())
                .ToList();
        }

        AssignHierarchyIds(organization);
    }

    private static void AssignHierarchyIds(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);

        if (organization.Id == Guid.Empty)
        {
            organization.Id = DeterministicIdHelper.CreateGuid("organization", organization.Name);
        }

        foreach (var department in organization.Departments)
        {
            if (department.Id == Guid.Empty)
            {
                department.Id = DeterministicIdHelper.CreateGuid(
                    "department",
                    organization.Id.ToString("N"),
                    department.Name.ToString());
            }
            department.OrganizationId = organization.Id;

            foreach (var role in department.Roles)
            {
                if (role.Id == Guid.Empty)
                {
                    role.Id = DeterministicIdHelper.CreateGuid(
                        "role",
                        organization.Id.ToString("N"),
                        department.Name.ToString(),
                        role.Name.ToString());
                }
                role.DepartmentId = department.Id;
                role.OrganizationId = organization.Id;

                foreach (var character in role.Characters)
                {
                    if (character.Id == Guid.Empty)
                    {
                        character.Id = DeterministicIdHelper.CreateGuid(
                            "character",
                            organization.Id.ToString("N"),
                            character.Email.ToLowerInvariant());
                    }
                    character.RoleId = role.Id;
                    character.DepartmentId = department.Id;
                    character.OrganizationId = organization.Id;
                }
            }
        }
    }

    internal static void EnsureCaseParties(List<Organization> organizations)
    {
        if (organizations.Count == 0)
            return;

        if (!organizations.Any(o => o.IsPlaintiff))
        {
            organizations[0].IsPlaintiff = true;
        }

        if (!organizations.Any(o => o.IsDefendant) && organizations.Count > 1)
        {
            var candidate = organizations.FirstOrDefault(o => !o.IsPlaintiff) ?? organizations[0];
            if (!candidate.IsPlaintiff)
                candidate.IsDefendant = true;
        }
    }

    internal static OrganizationType NormalizeOrganizationType(OrganizationType type, string name)
    {
        if (type != OrganizationType.Unknown)
            return type;

        var index = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(name)) % OrganizationTypes.Length;
        return OrganizationTypes[index];
    }

    internal static UsState NormalizeState(UsState state, string name)
    {
        if (state != UsState.Unknown)
            return state;

        var index = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(name)) % UsStates.Length;
        return UsStates[index];
    }

    private static string NormalizeDomain(string? domain, string organizationName, HashSet<string> usedDomains)
    {
        var normalized = string.IsNullOrWhiteSpace(domain)
            ? string.Empty
            : domain.Trim().ToLowerInvariant();

        if ((normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            normalized = uri.Host;
        }

        normalized = normalized.Trim('.');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = GenerateDomainFromName(organizationName);
        }

        if (!IsValidDomain(normalized))
        {
            normalized = GenerateDomainFromName(organizationName);
        }

        if (!IsValidDomain(normalized))
        {
            normalized = "organization.com";
        }

        var candidate = normalized;
        var counter = 2;
        while (!usedDomains.Add(candidate))
        {
            candidate = InsertDomainSuffix(normalized, counter++);
        }

        return candidate;
    }

    internal static string GenerateDomainFromName(string organizationName)
    {
        var cleaned = new string(organizationName
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-')
            .ToArray())
            .Replace("  ", " ")
            .Replace(" ", "-")
            .Trim('-');

        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "organization";

        if (cleaned.Length > 63)
        {
            cleaned = cleaned[..63].TrimEnd('-');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = "organization";
            }
        }

        return cleaned + ".com";
    }

    private static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;
        if (domain.Length > 253 || !domain.Contains('.') || domain.Contains('@'))
            return false;

        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
            return false;

        return labels.All(IsValidDomainLabel);
    }

    private static bool IsValidDomainLabel(string label)
    {
        if (label.Length == 0 || label.Length > 63)
            return false;
        if (label.StartsWith('-') || label.EndsWith('-'))
            return false;

        foreach (var ch in label)
        {
            if (ch > 0x7F)
                return false;

            var lower = char.ToLowerInvariant(ch);
            if (!((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9') || lower == '-'))
                return false;
        }

        return true;
    }

    private static string InsertDomainSuffix(string domain, int counter)
    {
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0)
            return domain + counter;

        var suffix = "-" + counter;
        var labelIndex = labels.Length >= 2 ? labels.Length - 2 : labels.Length - 1;
        var baseLabel = labels[labelIndex];
        var maxBaseLength = Math.Max(1, 63 - suffix.Length);
        if (baseLabel.Length > maxBaseLength)
        {
            baseLabel = baseLabel[..maxBaseLength].TrimEnd('-');
        }

        if (string.IsNullOrWhiteSpace(baseLabel))
        {
            baseLabel = "org";
        }

        labels[labelIndex] = baseLabel + suffix;
        return string.Join(".", labels);
    }

    internal class OrganizationSeedResponse
    {
        [JsonPropertyName("entities")]
        public List<OrganizationSeedDto> Organizations { get; set; } = new();
    }

    internal class OrganizationSeedDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        public string? Domain { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("organizationType")]
        public string? OrganizationType { get; set; }

        [JsonPropertyName("industry")]
        public string? Industry { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("plaintiff")]
        public bool Plaintiff { get; set; }

        [JsonPropertyName("defendant")]
        public bool Defendant { get; set; }

        [JsonPropertyName("departments")]
        public List<DepartmentDto>? Departments { get; set; }
    }

    internal class OrganizationDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        public string? Domain { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("organizationType")]
        public string? OrganizationType { get; set; }

        [JsonPropertyName("industry")]
        public string? Industry { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("founded")]
        public string? Founded { get; set; }

        [JsonPropertyName("plaintiff")]
        public bool Plaintiff { get; set; }

        [JsonPropertyName("defendant")]
        public bool Defendant { get; set; }

        [JsonPropertyName("departments")]
        public List<DepartmentDto>? Departments { get; set; }
    }

    internal class DepartmentDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("roles")]
        public List<RoleDto>? Roles { get; set; }
    }

    internal class RoleDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("reportsToRole")]
        public string? ReportsToRole { get; set; }
    }
}
