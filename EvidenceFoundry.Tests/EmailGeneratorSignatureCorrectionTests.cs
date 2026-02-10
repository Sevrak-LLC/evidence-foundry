using System.Reflection;
using EvidenceFoundry.Models;
using EvidenceFoundry.Services;

namespace EvidenceFoundry.Tests;

public class EmailGeneratorSignatureCorrectionTests
{
    [Fact]
    public void CorrectSignatureBlock_ReplacesWrongSignatureBlock()
    {
        var fromChar = new Character
        {
            FirstName = "Alice",
            LastName = "Smith",
            SignatureBlock = "Alice Smith\nAcme"
        };
        var otherChar = new Character
        {
            FirstName = "Bob",
            LastName = "Jones",
            SignatureBlock = "Bob Jones\nContoso"
        };
        var body = "Hello team,\n\nBob Jones\nContoso";

        var result = InvokeCorrectSignatureBlock(body, fromChar, new List<Character> { fromChar, otherChar });

        Assert.Contains(fromChar.SignatureBlock, result, StringComparison.Ordinal);
        Assert.DoesNotContain(otherChar.SignatureBlock, result, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectSignatureBlock_ReplacesWrongNameWithSignOff()
    {
        var fromChar = new Character
        {
            FirstName = "Alice",
            LastName = "Smith",
            SignatureBlock = "Alice Smith\nAcme"
        };
        var otherChar = new Character
        {
            FirstName = "Bob",
            LastName = "Jones"
        };
        var body = "Update below.\n\nBest,\nBob Jones";

        var result = InvokeCorrectSignatureBlock(body, fromChar, new List<Character> { fromChar, otherChar });

        Assert.Contains(fromChar.SignatureBlock, result, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob Jones", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectSignatureBlock_ReplacesSignatureWhenSenderMissingFromTail()
    {
        var fromChar = new Character
        {
            FirstName = "Alice",
            LastName = "Smith",
            SignatureBlock = "Alice Smith\nAcme"
        };
        var body = "Quick note.\n\nThanks,\nUnknown";

        var result = InvokeCorrectSignatureBlock(body, fromChar, new List<Character> { fromChar });

        Assert.Contains(fromChar.SignatureBlock, result, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown", result, StringComparison.Ordinal);
    }

    private static string InvokeCorrectSignatureBlock(
        string body,
        Character fromChar,
        List<Character> allCharacters)
    {
        var method = typeof(EmailGenerator).GetMethod(
            "CorrectSignatureBlock",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return (string)method.Invoke(null, new object[] { body, fromChar, allCharacters })!;
    }
}
