using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using EvidenceFoundry.Models;

namespace EvidenceFoundry.Services;

public class OfficeDocumentService
{
    public byte[] CreateWordDocument(string title, string content, OrganizationTheme? theme = null)
    {
        var t = theme ?? OrganizationTheme.Default;
        var safeTitle = SanitizeXmlText(title);
        var safeContent = SanitizeXmlText(content);
        var safeOrgName = SanitizeXmlText(t.OrganizationName ?? string.Empty);

        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Add page margins
            var sectionProps = new SectionProperties(
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440 }
            );

            // Add header with organization branding (colored bar with org name)
            if (!string.IsNullOrEmpty(safeOrgName))
            {
                var headerPara = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = t.PrimaryColor },
                        new SpacingBetweenLines { After = "200" },
                        new Indentation { Left = "-115", Right = "-115" }
                    )
                ));
                var headerRun = headerPara.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "24" },
                        new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.TextLight },
                        new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.HeadingFont, HighAnsi = t.HeadingFont }
                    ),
                    new DocumentFormat.OpenXml.Wordprocessing.Text($"  {safeOrgName}")
                ));

                // Add accent line under header
                var accentPara = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = t.AccentColor },
                        new SpacingBetweenLines { After = "400", Line = "60", LineRule = LineSpacingRuleValues.Exact },
                        new Indentation { Left = "-115", Right = "-115" }
                    )
                ));
            }

            // Add document title with themed color
            var titlePara = body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { After = "200" }
                )
            ));
            var titleRun = titlePara.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run());
            titleRun.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "36" },
                new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.PrimaryColor },
                new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.HeadingFont, HighAnsi = t.HeadingFont }
            ));
            titleRun.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(safeTitle));

            // Add subtle divider line
            var dividerPara = body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(
                        new DocumentFormat.OpenXml.Wordprocessing.BottomBorder { Val = BorderValues.Single, Size = 6, Color = t.SecondaryColor }
                    ),
                    new SpacingBetweenLines { After = "300" }
                )
            ));

            // Add content paragraphs with better typography
            var paragraphs = safeContent.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var para in paragraphs)
            {
                var p = body.AppendChild(new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines { After = "200", Line = "276", LineRule = LineSpacingRuleValues.Auto }
                    )
                ));
                var run = p.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "22" },
                        new DocumentFormat.OpenXml.Wordprocessing.Color { Val = t.TextDark },
                        new DocumentFormat.OpenXml.Wordprocessing.RunFonts { Ascii = t.BodyFont, HighAnsi = t.BodyFont }
                    )
                ));

                // Handle line breaks within paragraphs
                var lines = para.Trim().Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
                    if (i < lines.Length - 1)
                    {
                        run.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Break());
                    }
                }
            }

            body.AppendChild(sectionProps);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    public byte[] CreateExcelDocument(string title, List<string> headers, List<List<string>> rows)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = SanitizeSheetName(title)
            };
            sheets.Append(sheet);

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;

            // Add header row
            var headerRow = new Row { RowIndex = 1 };
            for (int i = 0; i < headers.Count; i++)
            {
                headerRow.Append(CreateCell(GetColumnName(i + 1), 1, headers[i]));
            }
            sheetData.Append(headerRow);

            // Add data rows
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var dataRow = new Row { RowIndex = (uint)(rowIndex + 2) };
                for (int colIndex = 0; colIndex < rows[rowIndex].Count; colIndex++)
                {
                    dataRow.Append(CreateCell(GetColumnName(colIndex + 1), (uint)(rowIndex + 2), rows[rowIndex][colIndex]));
                }
                sheetData.Append(dataRow);
            }

            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    public byte[] CreatePowerPointDocument(string title, List<(string slideTitle, string content)> slides, OrganizationTheme? theme = null)
    {
        // Use provided theme or default colors
        var t = theme ?? OrganizationTheme.Default;
        var safeTitle = SanitizeXmlText(title);

        using var stream = new MemoryStream();
        using (var doc = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, true))
        {
            // Create presentation part with proper structure
            var presentationPart = doc.AddPresentationPart();

            // Create slide master part first
            var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();

            // Create theme part (required) - link to presentation AND slide master
            var themePart = presentationPart.AddNewPart<ThemePart>();
            themePart.Theme = CreateModernTheme(t);
            themePart.Theme.Save();

            // Also add theme to slide master
            slideMasterPart.AddPart(themePart);

            // Create slide layout part
            var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
            slideLayoutPart.SlideLayout = CreateSlideLayout();
            slideLayoutPart.SlideLayout.Save();

            // Create slide master
            slideMasterPart.SlideMaster = CreateSlideMaster(slideMasterPart.GetIdOfPart(slideLayoutPart));
            slideMasterPart.SlideMaster.Save();

            // Build the presentation with all required elements in correct order
            var presentation = new P.Presentation();
            presentation.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
            presentation.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            presentation.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");

            // Add slide master id list
            presentation.AppendChild(new P.SlideMasterIdList(
                new P.SlideMasterId { Id = 2147483648, RelationshipId = presentationPart.GetIdOfPart(slideMasterPart) }
            ));

            // Add slide id list
            var slideIdList = presentation.AppendChild(new P.SlideIdList());

            // Add slide size and notes size
            presentation.AppendChild(new P.SlideSize { Cx = 9144000, Cy = 6858000, Type = P.SlideSizeValues.Screen4x3 });
            presentation.AppendChild(new P.NotesSize { Cx = 6858000, Cy = 9144000 });

            // Add default text style
            presentation.AppendChild(new P.DefaultTextStyle());

            presentationPart.Presentation = presentation;

            uint slideId = 256;

            // Create title slide (special formatting)
            CreateTitleSlide(presentationPart, slideLayoutPart, slideIdList, slideId++, safeTitle, "Generated Email Dataset", t);

            // Create content slides
            foreach (var (slideTitle, content) in slides)
            {
                CreateContentSlide(
                    presentationPart,
                    slideLayoutPart,
                    slideIdList,
                    slideId++,
                    SanitizeXmlText(slideTitle),
                    SanitizeXmlText(content),
                    t);
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static string SanitizeSheetName(string? title)
    {
        var name = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Sheet1";
        }

        var invalidChars = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        foreach (var ch in invalidChars)
        {
            name = name.Replace(ch, '_');
        }

        name = name.Trim('\'');
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Sheet1";
        }

        return name.Length > 31 ? name[..31] : name;
    }

    private static A.Theme CreateModernTheme(OrganizationTheme t)
    {
        return new A.Theme(
            new A.ThemeElements(
                new A.ColorScheme(
                    new A.Dark1Color(new A.RgbColorModelHex { Val = t.TextDark }),
                    new A.Light1Color(new A.RgbColorModelHex { Val = t.TextLight }),
                    new A.Dark2Color(new A.RgbColorModelHex { Val = t.PrimaryColor }),
                    new A.Light2Color(new A.RgbColorModelHex { Val = t.BackgroundLight }),
                    new A.Accent1Color(new A.RgbColorModelHex { Val = t.SecondaryColor }),
                    new A.Accent2Color(new A.RgbColorModelHex { Val = t.AccentColor }),
                    new A.Accent3Color(new A.RgbColorModelHex { Val = "70AD47" }),  // Green
                    new A.Accent4Color(new A.RgbColorModelHex { Val = "7030A0" }),  // Purple
                    new A.Accent5Color(new A.RgbColorModelHex { Val = "00B0F0" }),  // Cyan
                    new A.Accent6Color(new A.RgbColorModelHex { Val = "FFC000" }),  // Gold
                    new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }),
                    new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" })
                )
                { Name = t.ThemeName },
                new A.FontScheme(
                    new A.MajorFont(
                        new A.LatinFont { Typeface = t.HeadingFont },
                        new A.EastAsianFont { Typeface = "" },
                        new A.ComplexScriptFont { Typeface = "" }
                    ),
                    new A.MinorFont(
                        new A.LatinFont { Typeface = t.BodyFont },
                        new A.EastAsianFont { Typeface = "" },
                        new A.ComplexScriptFont { Typeface = "" }
                    )
                )
                { Name = t.ThemeName },
                new A.FormatScheme(
                    new A.FillStyleList(
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })
                    ),
                    new A.LineStyleList(
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 9525 },
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 25400 },
                        new A.Outline(new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })) { Width = 38100 }
                    ),
                    new A.EffectStyleList(
                        new A.EffectStyle(new A.EffectList()),
                        new A.EffectStyle(new A.EffectList()),
                        new A.EffectStyle(new A.EffectList())
                    ),
                    new A.BackgroundFillStyleList(
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                        new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })
                    )
                )
                { Name = t.ThemeName }
            )
        )
        { Name = $"{t.ThemeName} Theme" };
    }

    private static Cell CreateCell(string columnName, uint rowIndex, string value)
    {
        var safeValue = SanitizeXmlText(value);
        return new Cell
        {
            CellReference = $"{columnName}{rowIndex}",
            CellValue = new CellValue(safeValue),
            DataType = CellValues.String
        };
    }

    private static string GetColumnName(int columnNumber)
    {
        string columnName = string.Empty;
        while (columnNumber > 0)
        {
            int modulo = (columnNumber - 1) % 26;
            columnName = Convert.ToChar('A' + modulo) + columnName;
            columnNumber = (columnNumber - modulo) / 26;
        }
        return columnName;
    }

    private static P.SlideMaster CreateSlideMaster(string slideLayoutRelId)
    {
        return new P.SlideMaster(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId { Id = 2147483649, RelationshipId = slideLayoutRelId }
            ));
    }

    private static P.SlideLayout CreateSlideLayout()
    {
        return new P.SlideLayout(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()))))
        { Type = P.SlideLayoutValues.Title };
    }

    private static void CreateTitleSlide(
        PresentationPart presentationPart,
        SlideLayoutPart slideLayoutPart,
        P.SlideIdList slideIdList,
        uint slideId,
        string title,
        string subtitle,
        OrganizationTheme t)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();

        // Title slide: Full themed background with centered white text
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.Background(
                    new P.BackgroundProperties(
                        new A.SolidFill(new A.RgbColorModelHex { Val = t.PrimaryColor }))),
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    // Main title - centered, large, white
                    CreateStyledTextShape(2, "Title", title, 457200, 2200000, 8229600, 1500000, 4800, t.TextLight, true, t.HeadingFont),
                    // Subtitle - centered, smaller, light gray
                    CreateStyledTextShape(3, "Subtitle", subtitle, 457200, 3800000, 8229600, 600000, 2400, "E0E0E0", false, t.BodyFont))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slidePart.AddPart(slideLayoutPart);
        slidePart.Slide.Save();

        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static void CreateContentSlide(
        PresentationPart presentationPart,
        SlideLayoutPart slideLayoutPart,
        P.SlideIdList slideIdList,
        uint slideId,
        string title,
        string content,
        OrganizationTheme t)
    {
        var slidePart = presentationPart.AddNewPart<SlidePart>();

        // Content slide: Themed header bar at top, white background for content
        slidePart.Slide = new P.Slide(
            new P.CommonSlideData(
                new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(
                        new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                        new P.NonVisualGroupShapeDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(new A.TransformGroup()),
                    // Header bar (themed rectangle at top)
                    CreateHeaderBar(2, 0, 0, 9144000, 1000000, t.PrimaryColor),
                    // Title text on the header bar (white)
                    CreateStyledTextShape(3, "Title", title, 300000, 250000, 8544000, 600000, 3200, t.TextLight, true, t.HeadingFont),
                    // Accent line under header
                    CreateAccentLine(4, 0, 1000000, 9144000, 40000, t.AccentColor),
                    // Content area (dark text on white background)
                    CreateContentTextShape(5, "Content", content, 300000, 1200000, 8544000, 5400000, 1800, t.TextDark, t.BodyFont))),
            new P.ColorMapOverride(new A.MasterColorMapping()));

        slidePart.AddPart(slideLayoutPart);
        slidePart.Slide.Save();

        slideIdList.Append(new P.SlideId
        {
            Id = slideId,
            RelationshipId = presentationPart.GetIdOfPart(slidePart)
        });
    }

    private static P.Shape CreateHeaderBar(uint id, long x, long y, long width, long height, string colorHex)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Header Bar" },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }),
                new A.Outline(new A.NoFill())));
    }

    private static P.Shape CreateAccentLine(uint id, long x, long y, long width, long height, string colorHex)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Accent Line" },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }),
                new A.Outline(new A.NoFill())));
    }

    private static P.Shape CreateStyledTextShape(uint id, string name, string text, long x, long y, long width, long height, int fontSize, string colorHex, bool bold, string fontName)
    {
        text = SanitizeXmlText(text);
        var runProps = new A.RunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Bold = bold,
            Dirty = false
        };
        runProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = colorHex }));
        runProps.AppendChild(new A.LatinFont { Typeface = fontName });

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            new P.TextBody(
                new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Center },
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center },
                    new A.Run(runProps, new A.Text(text)),
                    new A.EndParagraphRunProperties { Language = "en-US" })));
    }

    private static P.Shape CreateContentTextShape(uint id, string name, string text, long x, long y, long width, long height, int fontSize, string textColorHex, string fontName)
    {
        text = SanitizeXmlText(text);
        var runProps = new A.RunProperties
        {
            Language = "en-US",
            FontSize = fontSize,
            Dirty = false
        };
        runProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = textColorHex }));
        runProps.AppendChild(new A.LatinFont { Typeface = fontName });

        // Split content into paragraphs for better formatting
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        var textBody = new P.TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Top },
            new A.ListStyle());

        foreach (var para in paragraphs)
        {
            var lines = para.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var lineRunProps = new A.RunProperties
                {
                    Language = "en-US",
                    FontSize = fontSize,
                    Dirty = false
                };
                lineRunProps.AppendChild(new A.SolidFill(new A.RgbColorModelHex { Val = textColorHex }));
                lineRunProps.AppendChild(new A.LatinFont { Typeface = fontName });

                textBody.AppendChild(new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left },
                    new A.Run(lineRunProps, new A.Text(line.Trim())),
                    new A.EndParagraphRunProperties { Language = "en-US" }));
            }
            // Add spacing between paragraphs
            textBody.AppendChild(new A.Paragraph(new A.EndParagraphRunProperties { Language = "en-US" }));
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            textBody);
    }

    private static string SanitizeXmlText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    var codePoint = char.ConvertToUtf32(ch, value[i + 1]);
                    if (IsValidXmlCodePoint(codePoint))
                    {
                        sb.Append(ch);
                        sb.Append(value[i + 1]);
                    }
                    i++;
                }
                continue;
            }

            if (char.IsLowSurrogate(ch))
                continue;

            if (IsValidXmlCodePoint(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static bool IsValidXmlCodePoint(int codePoint)
    {
        return codePoint == 0x9
               || codePoint == 0xA
               || codePoint == 0xD
               || (codePoint >= 0x20 && codePoint <= 0xD7FF)
               || (codePoint >= 0xE000 && codePoint <= 0xFFFD)
               || (codePoint >= 0x10000 && codePoint <= 0x10FFFF);
    }
}
