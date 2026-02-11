namespace EvidenceFoundry.Helpers;

public static class HeaderValueHelper
{
    public static string SanitizeHeaderText(string? value, int maxLength = 256)
    {
        var trimmed = (value ?? string.Empty).Trim();
        trimmed = trimmed.Replace("\r", "").Replace("\n", "");
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    public static string SanitizeHeaderValue(string? value, int maxLength = 998)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Replace("\r", "").Replace("\n", "").Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
