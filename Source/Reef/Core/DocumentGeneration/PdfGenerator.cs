using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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
                                            text.Span("Page ");
                                            text.CurrentPageNumber();
                                            text.Span(" of ");
                                            text.TotalPages();
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
                                text.Span("Page ");
                                text.CurrentPageNumber();
                                text.Span(" of ");
                                text.TotalPages();
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
                .GeneratePdf(outputPath);

                var fileSize = new FileInfo(outputPath).Length;
                Log.Information("PDF generated successfully: {OutputPath}, Size: {FileSize} bytes", outputPath, fileSize);
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
        // Parse HTML-like content from Scriban rendering
        // For Phase 1, use simple text rendering
        // TODO Phase 4: Add proper HTML parsing with tables, styling, etc.
        
        var content = section.RenderedContent.Trim();
        
        // Simple HTML tag stripping for Phase 1
        content = System.Text.RegularExpressions.Regex.Replace(content, @"<[^>]+>", "");
        content = content.Replace("&nbsp;", " ")
                        .Replace("&lt;", "<")
                        .Replace("&gt;", ">")
                        .Replace("&amp;", "&");

        container.Padding(5).Text(content).FontSize(10);
    }
}
