namespace EvidenceFoundry.Helpers;

public static class PromptScaffolding
{
    public const string JsonOnlyInstruction =
        "Return ONLY valid JSON. No markdown, no commentary, no extra keys, no trailing commas. Use double quotes for all JSON strings and property names.";

    public static string AppendJsonOnlyInstruction(string prompt)
    {
        return JoinSections(prompt, JsonOnlyInstruction);
    }

    public static string JsonSchemaSection(string schema, string? preface = null)
    {
        if (string.IsNullOrWhiteSpace(schema))
            throw new ArgumentException("Schema is required.", nameof(schema));

        var content = string.IsNullOrWhiteSpace(preface)
            ? schema.TrimEnd()
            : $"{preface.TrimEnd()}\n{schema.TrimEnd()}";

        return Section("OUTPUT JSON SCHEMA (EXACT)", content);
    }

    public static string Section(string title, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return content.TrimEnd();

        return $"{title.TrimEnd()}\n{content.TrimEnd()}";
    }

    public static string JoinSections(params string[] sections)
    {
        if (sections == null || sections.Length == 0)
            return string.Empty;

        var cleaned = sections
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.TrimEnd());

        return string.Join("\n\n", cleaned);
    }
}
