using System.Security.Cryptography;
using System.Text;

namespace EvidenceFoundry.Helpers;

public static class DeterministicIdHelper
{
    private const string ScopePrefix = "EF-ID-v1";

    public static Guid CreateGuid(string scope, params string?[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(BuildPayload(scope, parts)));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static string CreateShortToken(string scope, int length, params string?[] parts)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(BuildPayload(scope, parts)));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return length >= hex.Length ? hex : hex[..length];
    }

    private static string BuildPayload(string scope, params string?[] parts)
    {
        var builder = new StringBuilder(ScopePrefix);
        builder.Append('|');
        builder.Append(scope);

        foreach (var part in parts)
        {
            builder.Append('|');
            builder.Append(NormalizePart(part));
        }

        return builder.ToString();
    }

    private static string NormalizePart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
