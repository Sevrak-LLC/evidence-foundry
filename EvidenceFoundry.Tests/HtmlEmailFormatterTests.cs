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

        Assert.Contains("<div class=\"forward-header-title\">---------- Forwarded message ----------</div>", html);
        Assert.Contains("<span class=\"forward-header-label\">From:</span> Alice &lt;alice@example.com&gt;", html);
        Assert.Contains("<span class=\"forward-header-label\">Subject:</span> Status", html);
        Assert.Contains("<p>The build is green.</p>", html);
        Assert.Contains("<p>Thanks,</p>", html);
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

        Assert.Contains("<ul>", html);
        Assert.Contains("<li>First item</li>", html);
        Assert.Contains("<ol>", html);
        Assert.Contains("<li>One</li>", html);
        Assert.Contains("<div class=\"signature\"", html);
        Assert.Contains("Jane Doe", html);
    }
}
