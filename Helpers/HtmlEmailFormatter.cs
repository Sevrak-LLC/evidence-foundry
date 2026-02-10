using EvidenceFoundry.Models;

namespace EvidenceFoundry.Helpers;

/// <summary>
/// Converts plain text email content to formatted HTML with organization-specific styling.
/// </summary>
public static class HtmlEmailFormatter
{
    /// <summary>
    /// Builds the HTML template with organization-specific styling.
    /// </summary>
    private static string BuildEmailTemplate(OrganizationTheme? theme)
    {
        var t = theme ?? OrganizationTheme.Default;

        // Build font stack based on theme
        var bodyFontStack = GetFontStack(t.BodyFont);
        var headingFontStack = GetFontStack(t.HeadingFont);

        // Convert hex colors to CSS format
        var primaryColor = $"#{t.PrimaryColor}";
        var secondaryColor = $"#{t.SecondaryColor}";
        var accentColor = $"#{t.AccentColor}";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        body {{
            font-family: {bodyFontStack};
            font-size: 11pt;
            line-height: 1.5;
            color: #{t.TextDark};
            margin: 0;
            padding: 0;
            background-color: #ffffff;
        }}
        .email-body {{
            max-width: 800px;
            padding: 20px;
        }}
        .signature {{
            margin-top: 20px;
            padding-top: 10px;
            border-top: 1px solid {secondaryColor};
            color: #444444;
            font-size: 10pt;
        }}
        .signature-line {{
            margin: 2px 0;
        }}
        .signature-line:first-child {{
            font-family: {headingFontStack};
            font-weight: 600;
            color: {primaryColor};
        }}
        .quoted-content {{
            margin-top: 20px;
            padding-left: 10px;
            border-left: 2px solid {primaryColor};
            color: #505050;
        }}
        .quoted-header {{
            color: {primaryColor};
            font-size: 10pt;
            margin-bottom: 10px;
        }}
        .forward-header {{
            margin-top: 20px;
            padding: 10px;
            background-color: #{t.BackgroundLight};
            border: 1px solid #e0e0e0;
            font-size: 10pt;
        }}
        .forward-header-title {{
            font-weight: bold;
            color: {primaryColor};
            margin-bottom: 5px;
        }}
        .forward-header-field {{
            margin: 2px 0;
        }}
        .forward-header-label {{
            font-weight: bold;
            color: #505050;
        }}
        p {{
            margin: 0 0 10px 0;
        }}
        ul, ol {{
            margin: 10px 0 10px 20px;
            padding-left: 20px;
        }}
        li {{
            margin: 4px 0;
            line-height: 1.5;
        }}
        ul li {{
            list-style-type: disc;
        }}
        ol li {{
            list-style-type: decimal;
        }}
        strong {{
            font-weight: 600;
        }}
        em {{
            font-style: italic;
        }}
        a {{
            color: {primaryColor};
            text-decoration: none;
        }}
        a:hover {{
            text-decoration: underline;
        }}
    </style>
</head>
<body style=""font-family: {bodyFontStack}; font-size: 11pt; line-height: 1.5; color: #{t.TextDark}; margin: 0; padding: 0; background-color: #ffffff;"">
    <div class=""email-body"" style=""max-width: 800px; padding: 20px; font-family: {bodyFontStack};"">
{{CONTENT}}
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Gets a CSS font stack for the given font name.
    /// </summary>
    private static string GetFontStack(string fontName)
    {
        // Serif fonts
        if (fontName.Contains("Georgia") || fontName.Contains("Times") ||
            fontName.Contains("Garamond") || fontName.Contains("Palatino") ||
            fontName.Contains("Book Antiqua"))
        {
            return $"'{fontName}', Georgia, 'Times New Roman', serif";
        }

        // Sans-serif fonts (default)
        return $"'{fontName}', 'Segoe UI', Calibri, Arial, sans-serif";
    }

    // Store current theme for inline element formatting
    [ThreadStatic]
    private static OrganizationTheme? _currentTheme;

    /// <summary>
    /// Converts plain text email body to formatted HTML with optional organization theming.
    /// </summary>
    /// <param name="plainText">The plain text email content.</param>
    /// <param name="theme">Optional organization theme for styling. Uses default if not provided.</param>
    public static string ConvertToHtml(string plainText, OrganizationTheme? theme = null)
    {
        // Store theme for use in inline element formatting
        _currentTheme = theme;

        if (string.IsNullOrEmpty(plainText))
            return WrapInTemplate("<p></p>", theme);

        // Split the email into parts: main content, quoted replies, forwarded content
        var (mainContent, quotedContent, isForward) = SplitEmailContent(plainText);

        var htmlContent = new System.Text.StringBuilder();

        // Format main content
        htmlContent.Append(FormatMainContent(mainContent));

        // Format quoted/forwarded content if present
        if (!string.IsNullOrEmpty(quotedContent))
        {
            if (isForward)
            {
                htmlContent.Append(FormatForwardedContent(quotedContent));
            }
            else
            {
                htmlContent.Append(FormatQuotedContent(quotedContent));
            }
        }

        return WrapInTemplate(htmlContent.ToString(), theme);
    }

    private static (string mainContent, string quotedContent, bool isForward) SplitEmailContent(string text)
    {
        // Look for forwarded message marker
        var forwardMarkers = new[]
        {
            "---------- Forwarded message ----------",
            "-------- Forwarded Message --------",
            "Begin forwarded message:"
        };

        foreach (var marker in forwardMarkers)
        {
            var forwardIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (forwardIndex > 0)
            {
                return (
                    text[..forwardIndex].TrimEnd(),
                    text[forwardIndex..],
                    true
                );
            }
        }

        // Look for quoted reply marker (On ... wrote:)
        var replyPatterns = new[]
        {
            "\n\nOn ",
            "\r\n\r\nOn "
        };

        foreach (var pattern in replyPatterns)
        {
            var replyIndex = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (replyIndex > 0)
            {
                // Check if there's a "wrote:" after "On "
                var wroteIndex = text.IndexOf(" wrote:", replyIndex, StringComparison.OrdinalIgnoreCase);
                if (wroteIndex > replyIndex)
                {
                    return (
                        text[..replyIndex].TrimEnd(),
                        text[replyIndex..],
                        false
                    );
                }
            }
        }

        return (text, string.Empty, false);
    }

    private static string FormatMainContent(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var html = new System.Text.StringBuilder();
        var inSignature = false;
        var signatureBuffer = new System.Text.StringBuilder();
        var signatureLineIndex = 0;
        var inBulletList = false;
        var inNumberedList = false;

        // Get theme for inline styles (email clients often strip <style> blocks)
        var t = _currentTheme ?? OrganizationTheme.Default;
        var primaryColor = $"#{t.PrimaryColor}";
        var secondaryColor = $"#{t.SecondaryColor}";
        var bodyFontStack = GetFontStack(t.BodyFont);
        var headingFontStack = GetFontStack(t.HeadingFont);

        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];

            // Detect signature start (common patterns)
            if (!inSignature && IsSignatureStart(lines, i))
            {
                // Close any open lists before signature
                if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }

                inSignature = true;
                signatureLineIndex = 0;
                // Add inline styles for signature block (email clients strip <style> blocks)
                signatureBuffer.AppendLine($"<div class=\"signature\" style=\"margin-top: 20px; padding-top: 10px; border-top: 1px solid {secondaryColor}; color: #444444; font-size: 10pt; font-family: {bodyFontStack};\">");
            }

            if (inSignature)
            {
                var encodedLine = System.Net.WebUtility.HtmlEncode(rawLine);
                if (string.IsNullOrWhiteSpace(encodedLine))
                {
                    signatureBuffer.AppendLine("<div class=\"signature-line\" style=\"margin: 2px 0;\">&nbsp;</div>");
                }
                else
                {
                    // First non-empty line of signature gets special styling (name)
                    if (signatureLineIndex == 0)
                    {
                        signatureBuffer.AppendLine($"<div class=\"signature-line\" style=\"margin: 2px 0; font-family: {headingFontStack}; font-weight: 600; color: {primaryColor};\">{encodedLine}</div>");
                    }
                    else
                    {
                        signatureBuffer.AppendLine($"<div class=\"signature-line\" style=\"margin: 2px 0;\">{encodedLine}</div>");
                    }
                    signatureLineIndex++;
                }
            }
            else
            {
                // Check for list patterns before encoding
                var isBulletItem = IsBulletListItem(rawLine, out var bulletContent);
                var isNumberedItem = IsNumberedListItem(rawLine, out var numberedContent);

                if (isBulletItem)
                {
                    // Close numbered list if switching
                    if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }

                    // Start bullet list if not already in one
                    if (!inBulletList) { html.AppendLine("<ul>"); inBulletList = true; }

                    var formattedContent = FormatInlineElements(bulletContent);
                    html.AppendLine($"<li>{formattedContent}</li>");
                }
                else if (isNumberedItem)
                {
                    // Close bullet list if switching
                    if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }

                    // Start numbered list if not already in one
                    if (!inNumberedList) { html.AppendLine("<ol>"); inNumberedList = true; }

                    var formattedContent = FormatInlineElements(numberedContent);
                    html.AppendLine($"<li>{formattedContent}</li>");
                }
                else
                {
                    // Close any open lists
                    if (inBulletList) { html.AppendLine("</ul>"); inBulletList = false; }
                    if (inNumberedList) { html.AppendLine("</ol>"); inNumberedList = false; }

                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        // Preserve blank lines as paragraph breaks
                        if (i > 0 && i < lines.Length - 1)
                        {
                            html.AppendLine("<p>&nbsp;</p>");
                        }
                    }
                    else
                    {
                        var formattedLine = FormatInlineElements(rawLine);
                        html.AppendLine($"<p>{formattedLine}</p>");
                    }
                }
            }
        }

        // Close any remaining open lists
        if (inBulletList) html.AppendLine("</ul>");
        if (inNumberedList) html.AppendLine("</ol>");

        if (inSignature)
        {
            signatureBuffer.AppendLine("</div>");
            html.Append(signatureBuffer);
        }

        return html.ToString();
    }

    private static bool IsBulletListItem(string line, out string content)
    {
        content = string.Empty;
        var trimmed = line.TrimStart();

        // Check for common bullet patterns: - , * , • , ○ , ▪
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") ||
            trimmed.StartsWith("• ") || trimmed.StartsWith("○ ") ||
            trimmed.StartsWith("▪ "))
        {
            content = trimmed[2..].Trim();
            return true;
        }

        // Check for checkbox patterns: [ ], [x], [X]
        if (trimmed.StartsWith("[ ] ") || trimmed.StartsWith("[x] ") || trimmed.StartsWith("[X] "))
        {
            var prefix = trimmed.StartsWith("[ ] ") ? "☐ " : "☑ ";
            content = prefix + trimmed[4..].Trim();
            return true;
        }

        return false;
    }

    private static bool IsNumberedListItem(string line, out string content)
    {
        content = string.Empty;
        var trimmed = line.TrimStart();

        // Check for numbered patterns: 1. , 2) , (1) , etc.
        for (int num = 1; num <= 20; num++)
        {
            var patterns = new[] { $"{num}. ", $"{num}) ", $"({num}) " };
            foreach (var pattern in patterns)
            {
                if (trimmed.StartsWith(pattern))
                {
                    content = trimmed[pattern.Length..].Trim();
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatInlineElements(string text)
    {
        // First HTML encode the text
        var encoded = System.Net.WebUtility.HtmlEncode(text);

        // Get theme colors
        var t = _currentTheme ?? OrganizationTheme.Default;
        var primaryColor = $"#{t.PrimaryColor}";
        var accentColor = $"#{t.AccentColor}";

        // Convert *text* to <strong>text</strong>
        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"\*([^*]+)\*",
            "<strong>$1</strong>");

        // Convert _text_ to <em>text</em>
        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"_([^_]+)_",
            "<em>$1</em>");

        // Convert URLs to clickable links (using theme primary color)
        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"(https?://[^\s<]+)",
            $"<a href=\"$1\" style=\"color: {primaryColor};\">$1</a>");

        // Convert ACTION REQUIRED: and similar to bold (using theme accent color)
        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"(ACTION REQUIRED:|URGENT:|IMPORTANT:|NOTE:|FYI:|REMINDER:)",
            $"<strong style=\"color: {accentColor};\">$1</strong>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return encoded;
    }

    private static bool IsSignatureStart(string[] lines, int index)
    {
        if (index >= lines.Length) return false;

        var line = lines[index].Trim();

        // Common signature delimiters
        if (line == "--" || line == "---" || line == "- -" || line.StartsWith("--"))
            return true;

        // Look for patterns like "Best regards," "Sincerely," etc. followed by a name
        var signoffPhrases = new[]
        {
            "Best regards",
            "Best,",
            "Regards,",
            "Sincerely,",
            "Thanks,",
            "Thank you,",
            "Cheers,",
            "Warm regards",
            "Kind regards",
            "All the best",
            "Take care",
            "Yours truly",
            "Respectfully",
            "Cordially"
        };

        foreach (var phrase in signoffPhrases)
        {
            if (line.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            {
                // Check if there's content after this that looks like a signature
                // (name, title, phone number, etc.)
                if (index + 1 < lines.Length)
                {
                    var nextLine = lines[index + 1].Trim();
                    if (!string.IsNullOrEmpty(nextLine) && !nextLine.StartsWith(">"))
                    {
                        return true;
                    }
                }
                return true;
            }
        }

        return false;
    }

    private static string FormatQuotedContent(string quotedText)
    {
        var html = new System.Text.StringBuilder();
        var lines = quotedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        html.AppendLine("<div class=\"quoted-content\">");

        bool headerWritten = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // First line with "On ... wrote:" becomes the header
            if (!headerWritten && line.StartsWith("On ") && line.Contains(" wrote:"))
            {
                html.AppendLine($"<div class=\"quoted-header\">{System.Net.WebUtility.HtmlEncode(line)}</div>");
                headerWritten = true;
                continue;
            }

            // Remove leading ">" from quoted lines
            var content = line.TrimStart('>', ' ');
            if (string.IsNullOrWhiteSpace(content))
            {
                html.AppendLine("<p>&nbsp;</p>");
            }
            else
            {
                html.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(content)}</p>");
            }
        }

        html.AppendLine("</div>");
        return html.ToString();
    }

    private static string FormatForwardedContent(string forwardedText)
    {
        var html = new System.Text.StringBuilder();
        var lines = forwardedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        html.AppendLine("<div class=\"forward-header\">");
        html.AppendLine("<div class=\"forward-header-title\">---------- Forwarded message ----------</div>");

        bool inHeader = true;
        var bodyContent = new System.Text.StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip the original marker line
            if (line.Contains("Forwarded message") || line.Contains("Begin forwarded message"))
                continue;

            // Header fields end with an empty line
            if (inHeader)
            {
                if (string.IsNullOrEmpty(line))
                {
                    inHeader = false;
                    html.AppendLine("</div>");
                    html.AppendLine("<div class=\"quoted-content\">");
                    continue;
                }

                // Parse header fields (From:, To:, Date:, Subject:)
                if (line.Contains(":"))
                {
                    var colonIndex = line.IndexOf(':');
                    var label = line[..colonIndex];
                    var value = line[(colonIndex + 1)..].Trim();
                    html.AppendLine($"<div class=\"forward-header-field\"><span class=\"forward-header-label\">{System.Net.WebUtility.HtmlEncode(label)}:</span> {System.Net.WebUtility.HtmlEncode(value)}</div>");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    bodyContent.AppendLine("<p>&nbsp;</p>");
                }
                else
                {
                    bodyContent.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
                }
            }
        }

        if (inHeader)
        {
            html.AppendLine("</div>");
            html.AppendLine("<div class=\"quoted-content\">");
        }

        html.Append(bodyContent);
        html.AppendLine("</div>");

        return html.ToString();
    }

    private static string WrapInTemplate(string content, OrganizationTheme? theme)
    {
        var template = BuildEmailTemplate(theme);
        return template.Replace("{CONTENT}", content);
    }
}
