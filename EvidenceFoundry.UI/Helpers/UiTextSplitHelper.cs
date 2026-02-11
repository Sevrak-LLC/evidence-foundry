namespace EvidenceFoundry.Helpers;

public static class UiTextSplitHelper
{
    private static readonly string[] LineSeparatorValues = { "\r\n", "\n" };

    public static ReadOnlySpan<string> LineSeparators => LineSeparatorValues;

    public static string[] SplitLines(string text, StringSplitOptions options)
    {
        return text.Split(LineSeparatorValues, options);
    }
}
