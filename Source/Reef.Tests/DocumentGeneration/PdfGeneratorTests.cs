using FluentAssertions;
using Reef.Core.DocumentGeneration;
using Xunit;

namespace Reef.Tests.DocumentGeneration;

public class PdfGeneratorTests
{
    private readonly PdfGenerator _generator;

    public PdfGeneratorTests()
    {
        _generator = new PdfGenerator();
    }

    [Fact]
    public void OutputFormat_ShouldBePDF()
    {
        // Act
        var format = _generator.OutputFormat;

        // Assert
        format.Should().Be("PDF");
    }

    [Fact]
    public async Task GenerateAsync_WithValidData_ShouldCreatePdfFile()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new() { { "invoice_number", "INV-001" }, { "customer_name", "Test Customer" } }
        };

        var layout = new DocumentLayout
        {
            OutputFormat = "PDF",
            PageSetup = new PageSetup
            {
                Size = "A4",
                Orientation = "Portrait"
            },
            Sections = new List<DocumentSection>
            {
                new()
                {
                    Name = "content",
                    RenderedContent = "<h1>Test Invoice</h1><p>Invoice Number: INV-001</p>"
                }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, fileSize, errorMessage) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue(errorMessage);
            fileSize.Should().BeGreaterThan(0);
            File.Exists(tempPath).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithMultipleSections_ShouldIncludeHeaderAndFooter()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new() { { "company_name", "ACME Corp" } }
        };

        var layout = new DocumentLayout
        {
            OutputFormat = "PDF",
            Sections = new List<DocumentSection>
            {
                new() { Name = "header", RenderedContent = "<div>ACME Corp</div>" },
                new() { Name = "content", RenderedContent = "<h1>Document Content</h1>" },
                new() { Name = "footer", RenderedContent = "<div>Footer Text</div>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, fileSize, errorMessage) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue(errorMessage);
            File.Exists(tempPath).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithLandscapeOrientation_ShouldSucceed()
    {
        // Arrange
        var data = new List<Dictionary<string, object>> { new() };
        var layout = new DocumentLayout
        {
            PageSetup = new PageSetup { Orientation = "Landscape" },
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = "<p>Landscape document</p>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, _, _) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithPageNumbers_ShouldIncludePageNumbers()
    {
        // Arrange
        var data = new List<Dictionary<string, object>> { new() };
        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = new string('x', 5000) } // Force multiple pages
            }
        };

        var options = new DocumentOptions
        {
            IncludePageNumbers = true,
            PageNumberFormat = "Page {page} of {total}"
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, _, _) = await _generator.GenerateAsync(data, layout, tempPath, options);

            // Assert
            success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithWatermark_ShouldSucceed()
    {
        // Arrange
        var data = new List<Dictionary<string, object>> { new() };
        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = "<h1>Confidential Document</h1>" }
            }
        };

        var options = new DocumentOptions
        {
            Watermark = "DRAFT"
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, _, _) = await _generator.GenerateAsync(data, layout, tempPath, options);

            // Assert
            success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData("A4")]
    [InlineData("Letter")]
    [InlineData("Legal")]
    public async Task GenerateAsync_WithDifferentPageSizes_ShouldSucceed(string pageSize)
    {
        // Arrange
        var data = new List<Dictionary<string, object>> { new() };
        var layout = new DocumentLayout
        {
            PageSetup = new PageSetup { Size = pageSize },
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = $"<p>Document with {pageSize} page size</p>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, _, _) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithEmptyData_ShouldStillSucceed()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>();
        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = "<p>Static content</p>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");

        try
        {
            // Act
            var (success, _, _) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
