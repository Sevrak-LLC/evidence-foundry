using System.Diagnostics;
using System.Text.Json.Serialization;
using EvidenceFoundry.Helpers;
using EvidenceFoundry.Models;
using Serilog;
using Serilog.Context;

namespace EvidenceFoundry.Services;

public partial class OrganizationGenerator
{
    private readonly OpenAIService _openAI;
    private readonly ILogger _logger;

    private static readonly OrganizationType[] OrganizationTypes = Enum.GetValues<OrganizationType>()
        .Where(t => t != OrganizationType.Unknown)
        .ToArray();

    private static readonly UsState[] UsStates = Enum.GetValues<UsState>()
        .Where(s => s != UsState.Unknown)
        .ToArray();


    public OrganizationGenerator(OpenAIService openAI, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openAI);
        _openAI = openAI;
        _logger = (logger ?? Serilog.Log.Logger).ForContext<OrganizationGenerator>();
        Log.OrganizationGeneratorInitialized(_logger);
    }

    public async Task<List<Organization>> GenerateKnownOrganizationsAsync(
        string topic,
        Storyline storyline,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic is required.", nameof(topic));
        ArgumentNullException.ThrowIfNull(storyline);
        if (string.IsNullOrWhiteSpace(storyline.Summary))
            throw new ArgumentException("Storyline summary is required.", nameof(storyline));

        using var topicScope = LogContext.PushProperty("Topic", topic);
        using var storylineIdScope = LogContext.PushProperty("StorylineId", storyline.Id);
        using var storylineTitleScope = LogContext.PushProperty("StorylineTitle", storyline.Title);

        var stopwatch = Stopwatch.StartNew();
        Log.GeneratingKnownOrganizations(_logger);

        try
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

            if (response?.Organizations == null || response.Organizations.Count == 0)
            {
                Log.OrganizationExtractionReturnedNoOrganizations(_logger, storyline.Title);
                throw new InvalidOperationException($"Organization extraction returned no organizations for storyline '{storyline.Title}'.");
            }

            var organizations = ParseSeedOrganizations(response);
            Log.GeneratedKnownOrganizations(_logger, organizations.Count, stopwatch.ElapsedMilliseconds);
            return organizations;
        }
        catch (OperationCanceledException)
        {
            Log.KnownOrganizationGenerationCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.KnownOrganizationGenerationFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
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
        ArgumentNullException.ThrowIfNull(storyline);
        if (!storyline.StartDate.HasValue)
            throw new ArgumentException("Storyline start date is required.", nameof(storyline));
        ArgumentNullException.ThrowIfNull(seed);

        using var organizationIdScope = LogContext.PushProperty("OrganizationId", seed.Id);
        using var organizationNameScope = LogContext.PushProperty("OrganizationName", seed.Name);
        using var storylineIdScope = LogContext.PushProperty("StorylineId", storyline.Id);
        using var storylineTitleScope = LogContext.PushProperty("StorylineTitle", storyline.Title);

        var stopwatch = Stopwatch.StartNew();
        Log.EnrichingOrganizationDetails(_logger);

        try
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

            var allowedDepartmentsJson = DepartmentGenerator.BuildAllowedDepartmentsJson(
                seed.Industry,
                seed.OrganizationType,
                _logger);
            var allowedDepartmentRoleMapJson = DepartmentGenerator.BuildAllowedDepartmentRoleMapJson(
                seed.Industry,
                seed.OrganizationType,
                _logger);
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

            var organization = BuildOrganizationFromResponse(seed, response);
            var roleCount = organization.Departments.Sum(d => d.Roles.Count);
            Log.EnrichedOrganization(
                _logger,
                organization.Departments.Count,
                roleCount,
                stopwatch.ElapsedMilliseconds);
            return organization;
        }
        catch (OperationCanceledException)
        {
            Log.OrganizationEnrichmentCanceled(_logger, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            Log.OrganizationEnrichmentFailed(_logger, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
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

        if (DateHelper.TryParseAiDate(response.Founded, out var founded))
        {
            organization.Founded = founded;
        }

        PopulateDepartments(organization, response.Departments);

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

        PopulateDepartments(organization, entity.Departments);
        return true;
    }

    private static void PopulateDepartments(
        Organization organization,
        IEnumerable<DepartmentDto>? departments)
    {
        if (departments == null)
            return;

        foreach (var deptDto in departments)
        {
            if (!EnumHelper.TryParseEnum(deptDto.Name, out DepartmentName deptName))
                continue;

            var department = new Department { Name = deptName };
            PopulateRoles(department, deptDto.Roles);
            organization.AddDepartment(department);
        }
    }

    private static void PopulateRoles(
        Department department,
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
            department.AddRole(role);
        }
    }

    internal static void NormalizeOrganization(
        Organization organization,
        DateTime storylineStartDate,
        HashSet<string> usedDomains,
        ILogger? logger = null)
    {
        var log = logger ?? Serilog.Log.Logger;

        if (string.IsNullOrWhiteSpace(organization.Name))
            throw new InvalidOperationException("Organization name is required.");

        if (organization.IsPlaintiff && organization.IsDefendant)
            throw new InvalidOperationException($"Organization '{organization.Name}' cannot be both plaintiff and defendant.");

        var originalDomain = organization.Domain;
        var originalOrgType = organization.OrganizationType;
        var originalState = organization.State;

        organization.Id = organization.Id == Guid.Empty
            ? DeterministicIdHelper.CreateGuid("organization", organization.Name)
            : organization.Id;

        organization.Domain = NormalizeDomain(organization.Domain, organization.Name, usedDomains);
        organization.Founded = DateHelper.NormalizeFoundedDate(organization.Founded, storylineStartDate);
        organization.OrganizationType = NormalizeOrganizationType(organization.OrganizationType, organization.Name);
        organization.State = NormalizeState(organization.State, organization.Name);
        DepartmentGenerator.ApplyDepartmentRoleConstraints(organization, log);

        if (!string.Equals(originalDomain, organization.Domain, StringComparison.OrdinalIgnoreCase))
        {
            Log.NormalizedOrganizationDomain(log, originalDomain, organization.Domain, organization.Name);
        }

        if (originalOrgType == OrganizationType.Unknown && organization.OrganizationType != OrganizationType.Unknown)
        {
            Log.AppliedDefaultOrganizationType(log, organization.OrganizationType, organization.Name);
        }

        if (originalState == UsState.Unknown && organization.State != UsState.Unknown)
        {
            Log.AppliedDefaultOrganizationState(log, organization.State, organization.Name);
        }

        if (organization.Departments.Count == 0)
        {
            var executive = new Department
            {
                Name = DepartmentName.Executive
            };
            executive.SetRoles(new List<Role> { new() { Name = RoleName.ChiefExecutiveOfficer } });
            organization.AddDepartment(executive);
            Log.OrganizationLackedDepartments(log, organization.Name);
        }

        RoleGenerator.EnsureSingleOccupantRolesInExecutive(organization, log);

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
                var defaults = DepartmentGenerator.GetAllowedRoles(
                    organization.Industry,
                    organization.OrganizationType,
                    department.Name,
                    log);
                if (defaults.Count > 0)
                    department.AddRole(new Role { Name = defaults[0] });
            }

            department.SetRoles(department.Roles
                .GroupBy(r => r.Name)
                .Select(g => g.First())
                .ToList());
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
        var normalized = EmailAddressHelper.TryNormalizeDomain(domain, out var parsedDomain)
            ? parsedDomain
            : GenerateDomainFromName(organizationName);

        if (!EmailAddressHelper.TryNormalizeDomain(normalized, out var validated))
        {
            normalized = "organization.com";
        }
        else
        {
            normalized = validated;
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

    private static class Log
    {
        public static void OrganizationGeneratorInitialized(ILogger logger)
            => logger.Debug("OrganizationGenerator initialized.");

        public static void GeneratingKnownOrganizations(ILogger logger)
            => logger.Information("Generating known organizations from storyline.");

        public static void OrganizationExtractionReturnedNoOrganizations(ILogger logger, string storylineTitle)
            => logger.Error(
                "Organization extraction returned no organizations for storyline {StorylineTitle}.",
                storylineTitle);

        public static void GeneratedKnownOrganizations(ILogger logger, int organizationCount, long durationMs)
            => logger.Information(
                "Generated {OrganizationCount} known organizations in {DurationMs} ms.",
                organizationCount,
                durationMs);

        public static void KnownOrganizationGenerationCanceled(ILogger logger, long durationMs)
            => logger.Warning("Known organization generation canceled after {DurationMs} ms.", durationMs);

        public static void KnownOrganizationGenerationFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Known organization generation failed after {DurationMs} ms.", durationMs);

        public static void EnrichingOrganizationDetails(ILogger logger)
            => logger.Information("Enriching organization details.");

        public static void EnrichedOrganization(
            ILogger logger,
            int departmentCount,
            int roleCount,
            long durationMs)
            => logger.Information(
                "Enriched organization with {DepartmentCount} departments and {RoleCount} roles in {DurationMs} ms.",
                departmentCount,
                roleCount,
                durationMs);

        public static void OrganizationEnrichmentCanceled(ILogger logger, long durationMs)
            => logger.Warning("Organization enrichment canceled after {DurationMs} ms.", durationMs);

        public static void OrganizationEnrichmentFailed(ILogger logger, long durationMs, Exception exception)
            => logger.Error(exception, "Organization enrichment failed after {DurationMs} ms.", durationMs);

        public static void NormalizedOrganizationDomain(
            ILogger logger,
            string originalDomain,
            string normalizedDomain,
            string organizationName)
            => logger.Warning(
                "Normalized organization domain from {OriginalDomain} to {NormalizedDomain} for {OrganizationName}.",
                originalDomain,
                normalizedDomain,
                organizationName);

        public static void AppliedDefaultOrganizationType(
            ILogger logger,
            OrganizationType organizationType,
            string organizationName)
            => logger.Warning(
                "Applied default organization type {OrganizationType} for {OrganizationName}.",
                organizationType,
                organizationName);

        public static void AppliedDefaultOrganizationState(ILogger logger, UsState state, string organizationName)
            => logger.Warning(
                "Applied default organization state {State} for {OrganizationName}.",
                state,
                organizationName);

        public static void OrganizationLackedDepartments(ILogger logger, string organizationName)
            => logger.Warning(
                "Organization {OrganizationName} lacked departments; added Executive with default role.",
                organizationName);
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
