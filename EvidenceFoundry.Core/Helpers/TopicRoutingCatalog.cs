using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

internal sealed class TopicRoutingCatalog
{
    private const string ResourcePrefix = "EvidenceFoundry.Resources.topic_routing";
    private static readonly Lazy<TopicRoutingCatalog> LazyInstance = new(Load);

    private readonly TopicRoutingTier _core;
    private readonly Dictionary<string, string> _roleFileIds;
    private readonly Dictionary<string, string> _departmentSlugs;
    private readonly byte[] _topicIndex;
    private readonly byte[] _topicBlob;
    private readonly string[] _resourceNames;
    private readonly ConcurrentDictionary<string, TopicRoutingTier?> _departmentCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TopicRoutingTier?> _roleCache = new(StringComparer.OrdinalIgnoreCase);

    private TopicRoutingCatalog(
        TopicRoutingTier core,
        Dictionary<string, string> roleFileIds,
        Dictionary<string, string> departmentSlugs,
        byte[] topicIndex,
        byte[] topicBlob,
        string[] resourceNames)
    {
        _core = core;
        _roleFileIds = roleFileIds;
        _departmentSlugs = departmentSlugs;
        _topicIndex = topicIndex;
        _topicBlob = topicBlob;
        _resourceNames = resourceNames;
    }

    public static TopicRoutingCatalog Instance => LazyInstance.Value;

    public TopicRoutingTier Core => _core;

    public bool TryGetRoleFileId(DepartmentName department, RoleName role, out string fileId)
    {
        var key = BuildRoleKey(department.ToString(), role.ToString());
        return _roleFileIds.TryGetValue(key, out fileId!);
    }

    public bool TryGetDepartmentSlug(DepartmentName department, out string slug)
    {
        var key = NormalizeKey(department.ToString());
        return _departmentSlugs.TryGetValue(key, out slug!);
    }

    public TopicRoutingTier? GetDepartmentRouting(string slug)
        => _departmentCache.GetOrAdd(slug, LoadDepartmentRouting);

    public TopicRoutingTier? GetRoleRouting(string fileId)
        => _roleCache.GetOrAdd(fileId, LoadRoleRouting);

    public bool TryGetTopicText(int topicId, out string topic)
    {
        topic = string.Empty;
        if (topicId < 0)
            return false;

        var entryOffset = topicId * 12;
        if (entryOffset < 0 || entryOffset + 12 > _topicIndex.Length)
            return false;

        var entrySpan = _topicIndex.AsSpan(entryOffset, 12);
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(entrySpan[..8]);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(entrySpan.Slice(8, 4));

        if (offset + length > (ulong)_topicBlob.Length)
            return false;

        var textBytes = _topicBlob.AsSpan((int)offset, (int)length);
        topic = Encoding.UTF8.GetString(textBytes);
        return !string.IsNullOrWhiteSpace(topic);
    }

    private static TopicRoutingCatalog Load()
    {
        var assembly = typeof(TopicRoutingCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        var core = LoadJsonResource<TopicRoutingTier>(
            assembly,
            resourceNames,
            $"{ResourcePrefix}.core.json",
            required: true) ?? new TopicRoutingTier();

        var roleMap = LoadJsonResource<RoleMapRoot>(
            assembly,
            resourceNames,
            $"{ResourcePrefix}.role-map.json",
            required: true) ?? new RoleMapRoot();

        var departments = LoadJsonResource<DepartmentListRoot>(
            assembly,
            resourceNames,
            $"{ResourcePrefix}.departments.json",
            required: true) ?? new DepartmentListRoot();

        var roleFileIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in roleMap.Targets)
        {
            if (string.IsNullOrWhiteSpace(entry.Department) || string.IsNullOrWhiteSpace(entry.Role))
                continue;
            if (string.IsNullOrWhiteSpace(entry.FileId))
                continue;

            var key = BuildRoleKey(entry.Department, entry.Role);
            roleFileIds[key] = entry.FileId;
        }

        var departmentSlugs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in departments.Departments)
        {
            if (string.IsNullOrWhiteSpace(entry.Department) || string.IsNullOrWhiteSpace(entry.Slug))
                continue;
            departmentSlugs[NormalizeKey(entry.Department)] = entry.Slug;
        }

        var topicIndex = LoadBinaryResource(
            assembly,
            resourceNames,
            $"{ResourcePrefix}.topics.idx");
        var topicBlob = LoadBinaryResource(
            assembly,
            resourceNames,
            $"{ResourcePrefix}.topics.bin");

        return new TopicRoutingCatalog(core, roleFileIds, departmentSlugs, topicIndex, topicBlob, resourceNames);
    }

    private static string NormalizeKey(string value)
        => value.Trim().ToLowerInvariant();

    private static string BuildRoleKey(string department, string role)
        => $"{NormalizeKey(department)}|{NormalizeKey(role)}";

    private TopicRoutingTier? LoadDepartmentRouting(string slug)
    {
        var resourceName = $"{ResourcePrefix}.departments.{slug}.json";
        return LoadJsonResource<TopicRoutingTier>(
            typeof(TopicRoutingCatalog).Assembly,
            _resourceNames,
            resourceName,
            required: false);
    }

    private TopicRoutingTier? LoadRoleRouting(string fileId)
    {
        var resourceName = $"{ResourcePrefix}.routes.{fileId}.json";
        return LoadJsonResource<TopicRoutingTier>(
            typeof(TopicRoutingCatalog).Assembly,
            _resourceNames,
            resourceName,
            required: false);
    }

    private static T? LoadJsonResource<T>(
        Assembly assembly,
        string[] resourceNames,
        string resourceName,
        bool required)
    {
        using var stream = TryOpenResourceStream(assembly, resourceNames, resourceName);
        if (stream == null)
        {
            if (required)
                throw new InvalidOperationException($"Missing topic-routing resource '{resourceName}'.");
            return default;
        }

        return JsonSerializer.Deserialize<T>(stream, JsonSerializationDefaults.CaseInsensitive);
    }

    private static byte[] LoadBinaryResource(Assembly assembly, string[] resourceNames, string resourceName)
    {
        using var stream = TryOpenResourceStream(assembly, resourceNames, resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Missing topic-routing resource '{resourceName}'.");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static Stream? TryOpenResourceStream(Assembly assembly, string[] resourceNames, string resourceName)
    {
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
            return stream;

        var fallback = resourceNames.FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        return fallback == null ? null : assembly.GetManifestResourceStream(fallback);
    }

    internal sealed class TopicRoutingTier
    {
        [JsonPropertyName("ss")]
        public TopicRoutingBucketsGroup? Send { get; init; }

        [JsonPropertyName("sr")]
        public TopicRoutingBucketsGroup? Receive { get; init; }
    }

    internal sealed class TopicRoutingBucketsGroup
    {
        [JsonPropertyName("i")]
        public List<int>? Internal { get; init; }

        [JsonPropertyName("e")]
        public List<int>? External { get; init; }

        [JsonPropertyName("b")]
        public List<int>? Both { get; init; }
    }

    private sealed class RoleMapRoot
    {
        [JsonPropertyName("targets")]
        public List<RoleMapEntry> Targets { get; init; } = new();
    }

    private sealed class RoleMapEntry
    {
        [JsonPropertyName("department")]
        public string Department { get; init; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("fileId")]
        public string FileId { get; init; } = string.Empty;
    }

    private sealed class DepartmentListRoot
    {
        [JsonPropertyName("departments")]
        public List<DepartmentEntry> Departments { get; init; } = new();
    }

    private sealed class DepartmentEntry
    {
        [JsonPropertyName("department")]
        public string Department { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; init; } = string.Empty;
    }
}
