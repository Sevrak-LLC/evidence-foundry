using EvidenceFoundry.Helpers;

namespace EvidenceFoundry.Tests;

public class HtmlEmailFormatterTests
{
    [Fact]
    public void ConvertToHtml_WithForwardedContent_RendersHeaderFieldsAndBody()
    {
        var input = """
Hello team,

---------- Forwarded message ----------
From: Alice <alice@example.com>
To: Bob <bob@example.com>
Date: Tue, Feb 4, 2025 at 10:00 AM
Subject: Status

The build is green.

Thanks,
Alice
""";

        var html = HtmlEmailFormatter.ConvertToHtml(input);

        Assert.Contains(
            "<div class=\"forward-header-title\">---------- Forwarded message ----------</div>",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<span class=\"forward-header-label\">From:</span> Alice &lt;alice@example.com&gt;",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "<span class=\"forward-header-label\">Subject:</span> Status",
            html,
            StringComparison.Ordinal);
        Assert.Contains("<p>The build is green.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<p>Thanks,</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConvertToHtml_WithListsAndSignature_RendersListsAndSignatureBlock()
    {
        var input = """
Hello team,

- First item
- Second item

1. One
2) Two

Best regards,
Jane Doe
Senior Analyst
""";

        var html = HtmlEmailFormatter.ConvertToHtml(input);

        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<li>First item</li>", html, StringComparison.Ordinal);
        Assert.Contains("<ol>", html, StringComparison.Ordinal);
        Assert.Contains("<li>One</li>", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"signature\"", html, StringComparison.Ordinal);
        Assert.Contains("Jane Doe", html, StringComparison.Ordinal);
    }
}
