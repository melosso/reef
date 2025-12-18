using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Reef.Core.DocumentGeneration.HtmlParsing;
using Serilog;

namespace Reef.Core.DocumentGeneration;

/// <summary>
/// DOCX document generator using Open XML SDK
/// License: MIT (DocumentFormat.OpenXml)
/// </summary>
public class DocxGenerator : IDocumentGenerator
{
    public string OutputFormat => "DOCX";

    public async Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> GenerateAsync(
        List<Dictionary<string, object>> data,
        DocumentLayout layoutDefinition,
        string outputPath,
        DocumentOptions? options = null)
    {
        return await Task.Run<(bool, long, string?)>(() =>
        {
            try
            {
                options ??= new DocumentOptions();

                using (WordprocessingDocument wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
                {
                    // Create main document part
                    MainDocumentPart mainPart = wordDoc.AddMainDocumentPart();
                    mainPart.Document = new Document();
                    Body body = mainPart.Document.AppendChild(new Body());

                    // Apply page setup
                    ApplyPageSetup(body, layoutDefinition.PageSetup);

                    // Render header section
                    var headerSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "header");
                    if (headerSection != null && !string.IsNullOrWhiteSpace(headerSection.RenderedContent))
                    {
                        CreateHeaderPart(mainPart, headerSection);
                    }

                    // Render content section
                    var contentSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "content");
                    if (contentSection != null && !string.IsNullOrWhiteSpace(contentSection.RenderedContent))
                    {
                        RenderSectionAsWordContent(body, contentSection, data);
                    }

                    // Render footer section
                    var footerSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "footer");
                    if (footerSection != null && !string.IsNullOrWhiteSpace(footerSection.RenderedContent) || options.IncludePageNumbers)
                    {
                        CreateFooterPart(mainPart, footerSection, options);
                    }

                    mainPart.Document.Save();
                }

                var fileSize = new FileInfo(outputPath).Length;
                Log.Information("DOCX generated successfully: {OutputPath}, Size: {FileSize} bytes", outputPath, fileSize);
                return (true, fileSize, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DOCX generation failed for {OutputPath}", outputPath);
                return (false, 0, ex.Message);
            }
        });
    }

    private void ApplyPageSetup(Body body, PageSetup setup)
    {
        var sectionProperties = new SectionProperties();

        // Page size (width and height in twentieths of a point)
        var (width, height) = setup.Size.ToUpperInvariant() switch
        {
            "A4" => (11906, 16838), // 210mm x 297mm
            "LETTER" => (12240, 15840), // 8.5" x 11"
            "LEGAL" => (12240, 20160), // 8.5" x 14"
            _ => (11906, 16838)
        };

        // Swap width/height for landscape
        if (setup.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase))
        {
            (width, height) = (height, width);
        }

        var pageSize = new PageSize
        {
            Width = (UInt32Value)(uint)width,
            Height = (UInt32Value)(uint)height,
            Orient = setup.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase) 
                ? PageOrientationValues.Landscape 
                : PageOrientationValues.Portrait
        };
        sectionProperties.Append(pageSize);

        // Page margins (convert mm to twentieths of a point: 1mm = 56.7 twips)
        var pageMargin = new PageMargin
        {
            Top = (int)(setup.Margins.Top * 56.7),
            Bottom = (int)(setup.Margins.Bottom * 56.7),
            Left = (UInt32)(uint)(setup.Margins.Left * 56.7),
            Right = (UInt32)(uint)(setup.Margins.Right * 56.7)
        };
        sectionProperties.Append(pageMargin);

        body.Append(sectionProperties);
    }

    private void CreateHeaderPart(MainDocumentPart mainPart, DocumentSection headerSection)
    {
        var headerPart = mainPart.AddNewPart<HeaderPart>();
        headerPart.Header = new Header();

        var paragraph = new Paragraph();
        var run = new Run();
        run.Append(new Text(StripHtml(headerSection.RenderedContent)));
        paragraph.Append(run);
        headerPart.Header.Append(paragraph);

        // Link header to document
        var sectPr = mainPart.Document.Body!.Elements<SectionProperties>().FirstOrDefault();
        if (sectPr == null)
        {
            sectPr = new SectionProperties();
            mainPart.Document.Body!.Append(sectPr);
        }

        var headerReference = new HeaderReference
        {
            Type = HeaderFooterValues.Default,
            Id = mainPart.GetIdOfPart(headerPart)
        };
        sectPr.PrependChild(headerReference);
    }

    private void CreateFooterPart(MainDocumentPart mainPart, DocumentSection? footerSection, DocumentOptions options)
    {
        var footerPart = mainPart.AddNewPart<FooterPart>();
        footerPart.Footer = new Footer();

        // Add custom footer content
        if (footerSection != null && !string.IsNullOrWhiteSpace(footerSection.RenderedContent))
        {
            var contentParagraph = new Paragraph();
            var contentRun = new Run();
            contentRun.Append(new Text(StripHtml(footerSection.RenderedContent)));
            contentParagraph.Append(contentRun);
            footerPart.Footer.Append(contentParagraph);
        }

        // Add page numbers
        if (options.IncludePageNumbers)
        {
            var pageNumParagraph = new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center }
                )
            );

            var pageNumRun = new Run();
            pageNumRun.Append(new Text("Page "));
            pageNumRun.Append(new SimpleField { Instruction = "PAGE" });
            pageNumRun.Append(new Text(" of "));
            pageNumRun.Append(new SimpleField { Instruction = "NUMPAGES" });

            pageNumParagraph.Append(pageNumRun);
            footerPart.Footer.Append(pageNumParagraph);
        }

        // Link footer to document
        var sectPr = mainPart.Document.Body!.Elements<SectionProperties>().FirstOrDefault();
        if (sectPr == null)
        {
            sectPr = new SectionProperties();
            mainPart.Document.Body!.Append(sectPr);
        }

        var footerReference = new FooterReference
        {
            Type = HeaderFooterValues.Default,
            Id = mainPart.GetIdOfPart(footerPart)
        };
        sectPr.PrependChild(footerReference);
    }

    private void RenderSectionAsWordContent(Body body, DocumentSection section, List<Dictionary<string, object>> data)
    {
        var content = section.RenderedContent;
        
        // Check if content contains HTML tables
        if (content.Contains("<table"))
        {
            RenderHtmlContent(body, content);
        }
        else if (content.Contains("<h") || content.Contains("<strong") || content.Contains("<b>") || content.Contains("<em") || content.Contains("<i>"))
        {
            // Contains HTML formatting
            RenderFormattedText(body, content);
        }
        else
        {
            // Plain text
            var plainText = StripHtml(content);
            var lines = plainText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var paragraph = new Paragraph();
                var run = new Run();
                run.Append(new Text(line.Trim()));
                paragraph.Append(run);
                body.Append(paragraph);
            }
        }
    }
    
    private void RenderHtmlContent(Body body, string html)
    {
        // Parse and render tables
        var tables = HtmlTableParser.ParseTables(html);
        var parts = SplitHtmlByTables(html);
        
        for (int i = 0; i < parts.Count; i++)
        {
            // Render text content
            if (!string.IsNullOrWhiteSpace(parts[i]))
            {
                RenderFormattedText(body, parts[i]);
            }
            
            // Render table if exists at this position
            if (i < tables.Count)
            {
                var tableData = tables[i];
                var table = new Table();
                
                // Table properties
                var tableProperties = new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                        new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                    )
                );
                table.AppendChild(tableProperties);
                
                // Header row
                if (tableData.Headers.Any())
                {
                    var headerRow = new TableRow();
                    foreach (var header in tableData.Headers)
                    {
                        var cell = new TableCell();
                        var cellProperties = new TableCellProperties(
                            new Shading { Val = ShadingPatternValues.Clear, Fill = "D3D3D3" }
                        );
                        cell.Append(cellProperties);
                        
                        var paragraph = new Paragraph();
                        var run = new Run(new RunProperties(new Bold()), new Text(header));
                        paragraph.Append(run);
                        cell.Append(paragraph);
                        headerRow.Append(cell);
                    }
                    table.Append(headerRow);
                }
                
                // Data rows
                foreach (var row in tableData.Rows)
                {
                    var tableRow = new TableRow();
                    foreach (var cellText in row)
                    {
                        var cell = new TableCell();
                        var paragraph = new Paragraph();
                        var run = new Run(new Text(cellText));
                        paragraph.Append(run);
                        cell.Append(paragraph);
                        tableRow.Append(cell);
                    }
                    table.Append(tableRow);
                }
                
                body.Append(table);
            }
        }
    }
    
    private void RenderFormattedText(Body body, string html)
    {
        var segments = HtmlTextParser.ParseFormattedText(html);
        
        Paragraph? currentParagraph = null;
        
        foreach (var segment in segments)
        {
            // Start new paragraph if needed
            if (currentParagraph == null || segment.Content.Contains("\n"))
            {
                if (currentParagraph != null)
                {
                    body.Append(currentParagraph);
                }
                currentParagraph = new Paragraph();
            }
            
            // Create run with formatting
            var runProperties = new RunProperties();
            
            if (segment.Style.IsBold)
                runProperties.Append(new Bold());
                
            if (segment.Style.IsItalic)
                runProperties.Append(new Italic());
                
            if (segment.Style.IsUnderline)
                runProperties.Append(new Underline { Val = UnderlineValues.Single });
                
            if (segment.Style.IsHeading)
                runProperties.Append(new FontSize { Val = (segment.Style.FontSize * 2).ToString() }); // Half-points
            
            var run = new Run(runProperties, new Text(segment.Content.Replace("\n", "")));
            currentParagraph.Append(run);
        }
        
        if (currentParagraph != null)
        {
            body.Append(currentParagraph);
        }
    }
    
    private List<string> SplitHtmlByTables(string html)
    {
        var parts = new List<string>();
        var remainingHtml = html;
        
        while (remainingHtml.Contains("<table"))
        {
            var tableStart = remainingHtml.IndexOf("<table");
            
            // Add text before table
            if (tableStart > 0)
            {
                parts.Add(remainingHtml.Substring(0, tableStart));
            }
            
            // Find table end
            var tableEnd = remainingHtml.IndexOf("</table>", tableStart);
            if (tableEnd == -1) break;
            
            tableEnd += "</table>".Length;
            remainingHtml = remainingHtml.Substring(tableEnd);
        }
        
        // Add remaining text
        if (!string.IsNullOrWhiteSpace(remainingHtml))
        {
            parts.Add(remainingHtml);
        }
        
        return parts;
    }

    private string StripHtml(string html)
    {
        // Simple HTML tag stripping
        var content = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", "");
        content = content.Replace("&nbsp;", " ")
                        .Replace("&lt;", "<")
                        .Replace("&gt;", ">")
                        .Replace("&amp;", "&");
        return content.Trim();
    }
}
