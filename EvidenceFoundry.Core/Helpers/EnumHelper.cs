namespace EvidenceFoundry.Helpers;

public static class EnumHelper
{
    public static string HumanizeEnumName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var builder = new System.Text.StringBuilder(value.Length + 6);
        builder.Append(value[0]);

        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            var previous = value[i - 1];
            var next = i + 1 < value.Length ? value[i + 1] : '\0';

            var isBoundary = char.IsUpper(current) &&
                             (!char.IsUpper(previous) || (next != '\0' && char.IsLower(next)));

            if (isBoundary)
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    public static string FormatEnumOptions<TEnum>() where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        return string.Join(", ", names.Select(name => $"{name} ({HumanizeEnumName(name)})"));
    }

    public static bool TryParseEnum<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result);
    }
}
