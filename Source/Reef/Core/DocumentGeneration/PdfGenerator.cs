using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Reef.Core.DocumentGeneration.HtmlParsing;
using Serilog;

namespace Reef.Core.DocumentGeneration;

/// <summary>
/// PDF document generator using QuestPDF Fluent API
/// QuestPDF Community License: Valid for businesses with < $1M USD annual revenue
/// </summary>
public class PdfGenerator : IDocumentGenerator
{
    private static bool _licenseConfigured = false;
    private static readonly object _licenseLock = new();

    public string OutputFormat => "PDF";

    public PdfGenerator()
    {
        // Configure QuestPDF Community License (thread-safe, one-time setup)
        if (!_licenseConfigured)
        {
            lock (_licenseLock)
            {
                if (!_licenseConfigured)
                {
                    QuestPDF.Settings.License = LicenseType.Community;
                    _licenseConfigured = true;
                    Log.Information("QuestPDF Community License configured (valid for < $1M revenue)");
                }
            }
        }
    }

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

                // Generate PDF to MemoryStream first to avoid file locking issues
                byte[] pdfBytes;
                using (var memoryStream = new MemoryStream())
                {
                    Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            // Apply page setup from layout definition
                            ApplyPageSetup(page, layoutDefinition.PageSetup);

                            // Header section (repeats on every page)
                            var headerSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "header");
                            if (headerSection != null && !string.IsNullOrWhiteSpace(headerSection.RenderedContent))
                            {
                                page.Header().Element(container =>
                                {
                                    RenderSectionContent(container, headerSection, data);
                                });
                            }

                            // Main content section
                            var contentSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "content");
                            if (contentSection != null && !string.IsNullOrWhiteSpace(contentSection.RenderedContent))
                            {
                                page.Content().Element(container =>
                                {
                                    RenderSectionContent(container, contentSection, data);
                                });
                            }

                            // Footer section (repeats on every page)
                            var footerSection = layoutDefinition.Sections.FirstOrDefault(s => s.Name == "footer");
                            if (footerSection != null && !string.IsNullOrWhiteSpace(footerSection.RenderedContent))
                            {
                                page.Footer().Element(container =>
                                {
                                    if (options.IncludePageNumbers)
                                    {
                                        container.Column(column =>
                                        {
                                            // Custom footer content
                                            column.Item().Element(c => RenderSectionContent(c, footerSection, data));
                                            
                                            // Page numbers
                                            column.Item().AlignCenter().Text(text =>
                                            {
                                                text.Span("Page ").FontSize(9);
                                                text.CurrentPageNumber().FontSize(9);
                                                text.Span(" of ").FontSize(9);
                                                text.TotalPages().FontSize(9);
                                            });
                                        });
                                    }
                                    else
                                    {
                                        RenderSectionContent(container, footerSection, data);
                                    }
                                });
                            }
                            else if (options.IncludePageNumbers)
                            {
                                // Page numbers only (no custom footer)
                                page.Footer().AlignCenter().Text(text =>
                                {
                                    text.Span("Page ").FontSize(9);
                                    text.CurrentPageNumber().FontSize(9);
                                    text.Span(" of ").FontSize(9);
                                    text.TotalPages().FontSize(9);
                                });
                            }

                            // Watermark (if specified)
                            if (!string.IsNullOrWhiteSpace(options.Watermark))
                            {
                                page.Foreground().AlignCenter().AlignMiddle().Rotate(-45).Text(options.Watermark)
                                    .FontSize(60)
                                    .FontColor(Colors.Grey.Lighten3)
                                    .Bold();
                            }
                        });
                    })
                    .GeneratePdf(memoryStream);

                    pdfBytes = memoryStream.ToArray();
                }

                // Write to file with proper disposal to avoid file locking
                // Use FileStream with explicit Flush to ensure file is fully written
                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fileStream.Write(pdfBytes, 0, pdfBytes.Length);
                    fileStream.Flush(true); // Flush to disk
                }
                
                var fileSize = pdfBytes.Length;
                Log.Debug("PDF generated successfully: {OutputPath}, Size: {FileSize} bytes", outputPath, fileSize);
                return (true, fileSize, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PDF generation failed for {OutputPath}", outputPath);
                return (false, 0, ex.Message);
            }
        });
    }

    private void ApplyPageSetup(PageDescriptor page, PageSetup setup)
    {
        // Apply page size and orientation
        if (setup.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase))
        {
            switch (setup.Size.ToUpperInvariant())
            {
                case "A4":
                    page.Size(PageSizes.A4.Landscape());
                    break;
                case "LETTER":
                    page.Size(PageSizes.Letter.Landscape());
                    break;
                case "LEGAL":
                    page.Size(PageSizes.Legal.Landscape());
                    break;
                default:
                    page.Size(PageSizes.A4.Landscape());
                    break;
            }
        }
        else
        {
            switch (setup.Size.ToUpperInvariant())
            {
                case "A4":
                    page.Size(PageSizes.A4);
                    break;
                case "LETTER":
                    page.Size(PageSizes.Letter);
                    break;
                case "LEGAL":
                    page.Size(PageSizes.Legal);
                    break;
                default:
                    page.Size(PageSizes.A4);
                    break;
            }
        }

        // Apply margins (convert from mm to points: 1mm = 2.83465 points)
        page.MarginTop(setup.Margins.Top, Unit.Millimetre);
        page.MarginBottom(setup.Margins.Bottom, Unit.Millimetre);
        page.MarginLeft(setup.Margins.Left, Unit.Millimetre);
        page.MarginRight(setup.Margins.Right, Unit.Millimetre);

        page.PageColor(Colors.White);
    }

    private void RenderSectionContent(IContainer container, DocumentSection section, List<Dictionary<string, object>> data)
    {
        var content = section.RenderedContent.Trim();
        
        // Check if content contains HTML tables
        if (content.Contains("<table"))
        {
            RenderHtmlContent(container, content);
        }
        else if (content.Contains("<h") || content.Contains("<strong") || content.Contains("<b>") || content.Contains("<em") || content.Contains("<i>"))
        {
            // Contains HTML formatting
            RenderFormattedText(container, content);
        }
        else
        {
            // Plain text - strip any remaining HTML
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", "");
            content = content.Replace("&nbsp;", " ")
                            .Replace("&lt;", "<")
                            .Replace("&gt;", ">")
                            .Replace("&amp;", "&");
            
            container.Padding(5).Text(content).FontSize(10);
        }
    }
    
    private void RenderHtmlContent(IContainer container, string html)
    {
        container.Column(column =>
        {
            // Parse and render tables
            var tables = HtmlTableParser.ParseTables(html);
            
            // Get text content before/between/after tables
            var parts = SplitHtmlByTables(html);
            
            for (int i = 0; i < parts.Count; i++)
            {
                // Render text content
                if (!string.IsNullOrWhiteSpace(parts[i].Text))
                {
                    column.Item().Element(c => RenderFormattedText(c, parts[i].Text));
                }
                
                // Render table if exists at this position
                if (i < tables.Count)
                {
                    column.Item().PaddingVertical(5).Table(table =>
                    {
                        var tableData = tables[i];
                        
                        // Define columns
                        table.ColumnsDefinition(columns =>
                        {
                            for (int j = 0; j < tableData.ColumnCount; j++)
                            {
                                columns.RelativeColumn();
                            }
                        });
                        
                        // Render headers if present
                        if (tableData.Headers.Any())
                        {
                            table.Header(header =>
                            {
                                foreach (var headerCell in tableData.Headers)
                                {
                                    header.Cell()
                                        .Background(Colors.Grey.Lighten2)
                                        .Border(1)
                                        .BorderColor(Colors.Grey.Medium)
                                        .Padding(5)
                                        .Text(headerCell)
                                        .FontSize(10)
                                        .Bold();
                                }
                            });
                        }
                        
                        // Render data rows
                        foreach (var row in tableData.Rows)
                        {
                            foreach (var cell in row)
                            {
                                table.Cell()
                                    .Border(1)
                                    .BorderColor(Colors.Grey.Lighten1)
                                    .Padding(5)
                                    .Text(cell)
                                    .FontSize(10);
                            }
                        }
                    });
                }
            }
        });
    }
    
    private void RenderFormattedText(IContainer container, string html)
    {
        var segments = HtmlTextParser.ParseFormattedText(html);
        
        container.Padding(5).Text(text =>
        {
            foreach (var segment in segments)
            {
                var span = text.Span(segment.Content);
                
                if (segment.Style.IsBold)
                    span.Bold();
                    
                if (segment.Style.IsItalic)
                    span.Italic();
                    
                if (segment.Style.IsHeading)
                    span.FontSize(segment.Style.FontSize).Bold();
                else
                    span.FontSize(segment.Style.FontSize);
            }
        });
    }
    
    private List<HtmlPart> SplitHtmlByTables(string html)
    {
        var parts = new List<HtmlPart>();
        var remainingHtml = html;
        
        while (remainingHtml.Contains("<table"))
        {
            var tableStart = remainingHtml.IndexOf("<table");
            
            // Add text before table
            if (tableStart > 0)
            {
                parts.Add(new HtmlPart { Text = remainingHtml.Substring(0, tableStart) });
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
            parts.Add(new HtmlPart { Text = remainingHtml });
        }
        
        return parts;
    }
    
    private class HtmlPart
    {
        public string Text { get; set; } = string.Empty;
    }
}
