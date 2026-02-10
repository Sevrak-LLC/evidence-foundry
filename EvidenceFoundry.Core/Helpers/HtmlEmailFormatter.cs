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
        var state = new MainContentState(lines, html, BuildMainContentTheme());

        for (int i = 0; i < lines.Length; i++)
        {
            state.LineIndex = i;
            ProcessMainContentLine(lines[i], state);
        }

        FinalizeMainContent(state);
        return state.Html.ToString();
    }

    private static MainContentTheme BuildMainContentTheme()
    {
        var t = _currentTheme ?? OrganizationTheme.Default;
        return new MainContentTheme(
            $"#{t.PrimaryColor}",
            $"#{t.SecondaryColor}",
            GetFontStack(t.BodyFont),
            GetFontStack(t.HeadingFont));
    }

    private static void ProcessMainContentLine(string rawLine, MainContentState state)
    {
        if (!state.InSignature && IsSignatureStart(state.Lines, state.LineIndex))
        {
            StartSignature(state);
        }

        if (state.InSignature)
        {
            AppendSignatureLine(state, rawLine);
            return;
        }

        AppendListOrParagraph(state, rawLine);
    }

    private static void StartSignature(MainContentState state)
    {
        CloseLists(state);
        state.InSignature = true;
        state.SignatureLineIndex = 0;
        var theme = state.Theme;
        state.SignatureBuffer.AppendLine(
            $"<div class=\"signature\" style=\"margin-top: 20px; padding-top: 10px; border-top: 1px solid {theme.SecondaryColor}; color: #444444; font-size: 10pt; font-family: {theme.BodyFontStack};\">");
    }

    private static void AppendSignatureLine(MainContentState state, string rawLine)
    {
        var encodedLine = System.Net.WebUtility.HtmlEncode(rawLine);
        if (string.IsNullOrWhiteSpace(encodedLine))
        {
            state.SignatureBuffer.AppendLine("<div class=\"signature-line\" style=\"margin: 2px 0;\">&nbsp;</div>");
            return;
        }

        if (state.SignatureLineIndex == 0)
        {
            var theme = state.Theme;
            state.SignatureBuffer.AppendLine(
                $"<div class=\"signature-line\" style=\"margin: 2px 0; font-family: {theme.HeadingFontStack}; font-weight: 600; color: {theme.PrimaryColor};\">{encodedLine}</div>");
        }
        else
        {
            state.SignatureBuffer.AppendLine($"<div class=\"signature-line\" style=\"margin: 2px 0;\">{encodedLine}</div>");
        }

        state.SignatureLineIndex++;
    }

    private static void AppendListOrParagraph(MainContentState state, string rawLine)
    {
        var isBulletItem = IsBulletListItem(rawLine, out var bulletContent);
        var isNumberedItem = IsNumberedListItem(rawLine, out var numberedContent);

        if (isBulletItem)
        {
            EnsureListState(state, listType: ListType.Bullet);
            var formattedContent = FormatInlineElements(bulletContent);
            state.Html.AppendLine($"<li>{formattedContent}</li>");
            return;
        }

        if (isNumberedItem)
        {
            EnsureListState(state, listType: ListType.Numbered);
            var formattedContent = FormatInlineElements(numberedContent);
            state.Html.AppendLine($"<li>{formattedContent}</li>");
            return;
        }

        CloseLists(state);

        if (string.IsNullOrWhiteSpace(rawLine))
        {
            if (state.LineIndex > 0 && state.LineIndex < state.Lines.Length - 1)
            {
                state.Html.AppendLine("<p>&nbsp;</p>");
            }
            return;
        }

        var formattedLine = FormatInlineElements(rawLine);
        state.Html.AppendLine($"<p>{formattedLine}</p>");
    }

    private static void EnsureListState(MainContentState state, ListType listType)
    {
        if (listType == ListType.Bullet)
        {
            if (state.InNumberedList)
            {
                state.Html.AppendLine("</ol>");
                state.InNumberedList = false;
            }

            if (!state.InBulletList)
            {
                state.Html.AppendLine("<ul>");
                state.InBulletList = true;
            }

            return;
        }

        if (state.InBulletList)
        {
            state.Html.AppendLine("</ul>");
            state.InBulletList = false;
        }

        if (!state.InNumberedList)
        {
            state.Html.AppendLine("<ol>");
            state.InNumberedList = true;
        }
    }

    private static void CloseLists(MainContentState state)
    {
        if (state.InBulletList)
        {
            state.Html.AppendLine("</ul>");
            state.InBulletList = false;
        }

        if (state.InNumberedList)
        {
            state.Html.AppendLine("</ol>");
            state.InNumberedList = false;
        }
    }

    private static void FinalizeMainContent(MainContentState state)
    {
        CloseLists(state);

        if (!state.InSignature)
        {
            return;
        }

        state.SignatureBuffer.AppendLine("</div>");
        state.Html.Append(state.SignatureBuffer);
    }

    private enum ListType
    {
        Bullet,
        Numbered
    }

    private sealed class MainContentState
    {
        public MainContentState(string[] lines, System.Text.StringBuilder html, MainContentTheme theme)
        {
            Lines = lines;
            Html = html;
            Theme = theme;
        }

        public string[] Lines { get; }

        public System.Text.StringBuilder Html { get; }

        public MainContentTheme Theme { get; }

        public System.Text.StringBuilder SignatureBuffer { get; } = new();

        public bool InSignature { get; set; }

        public int SignatureLineIndex { get; set; }

        public bool InBulletList { get; set; }

        public bool InNumberedList { get; set; }

        public int LineIndex { get; set; }
    }

    private readonly record struct MainContentTheme(
        string PrimaryColor,
        string SecondaryColor,
        string BodyFontStack,
        string HeadingFontStack);

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

        AppendForwardHeaderStart(html);

        var state = new ForwardedContentState(html);
        foreach (var rawLine in lines)
        {
            ProcessForwardedLine(rawLine.Trim(), state);
        }

        if (state.InHeader)
        {
            CloseForwardHeader(state);
        }

        html.Append(state.BodyContent);
        html.AppendLine("</div>");

        return html.ToString();
    }

    private static void AppendForwardHeaderStart(System.Text.StringBuilder html)
    {
        html.AppendLine("<div class=\"forward-header\">");
        html.AppendLine("<div class=\"forward-header-title\">---------- Forwarded message ----------</div>");
    }

    private static void ProcessForwardedLine(string line, ForwardedContentState state)
    {
        if (IsForwardedMarker(line))
        {
            return;
        }

        if (state.InHeader)
        {
            if (IsHeaderTerminator(line))
            {
                CloseForwardHeader(state);
                return;
            }

            if (TryParseHeaderField(line, out var label, out var value))
            {
                AppendForwardHeaderField(state.Html, label, value);
            }

            return;
        }

        AppendBodyLine(state.BodyContent, line);
    }

    private static bool IsForwardedMarker(string line)
    {
        return line.Contains("Forwarded message") || line.Contains("Begin forwarded message");
    }

    private static bool IsHeaderTerminator(string line)
    {
        return string.IsNullOrEmpty(line);
    }

    private static bool TryParseHeaderField(string line, out string label, out string value)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
        {
            label = string.Empty;
            value = string.Empty;
            return false;
        }

        label = line[..colonIndex];
        value = line[(colonIndex + 1)..].Trim();
        return true;
    }

    private static void AppendForwardHeaderField(System.Text.StringBuilder html, string label, string value)
    {
        html.AppendLine(
            $"<div class=\"forward-header-field\"><span class=\"forward-header-label\">{System.Net.WebUtility.HtmlEncode(label)}:</span> {System.Net.WebUtility.HtmlEncode(value)}</div>");
    }

    private static void AppendBodyLine(System.Text.StringBuilder bodyContent, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            bodyContent.AppendLine("<p>&nbsp;</p>");
            return;
        }

        bodyContent.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
    }

    private static void CloseForwardHeader(ForwardedContentState state)
    {
        state.InHeader = false;
        state.Html.AppendLine("</div>");
        state.Html.AppendLine("<div class=\"quoted-content\">");
    }

    private sealed class ForwardedContentState
    {
        public ForwardedContentState(System.Text.StringBuilder html)
        {
            Html = html;
        }

        public bool InHeader { get; set; } = true;

        public System.Text.StringBuilder Html { get; }

        public System.Text.StringBuilder BodyContent { get; } = new();
    }

    private static string WrapInTemplate(string content, OrganizationTheme? theme)
    {
        var template = BuildEmailTemplate(theme);
        return template.Replace("{CONTENT}", content);
    }
}
