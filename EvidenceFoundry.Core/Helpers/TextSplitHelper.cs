namespace EvidenceFoundry.Helpers;

public static class TextSplitHelper
{
    public static readonly string[] LineSeparators = { "\r\n", "\n" };

    public static string[] SplitLines(string text, StringSplitOptions options)
    {
        return text.Split(LineSeparators, options);
    }
}
