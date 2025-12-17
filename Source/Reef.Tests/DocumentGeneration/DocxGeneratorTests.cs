using FluentAssertions;
using Reef.Core.DocumentGeneration;
using Xunit;

namespace Reef.Tests.DocumentGeneration;

public class DocxGeneratorTests
{
    private readonly DocxGenerator _generator;

    public DocxGeneratorTests()
    {
        _generator = new DocxGenerator();
    }

    [Fact]
    public void OutputFormat_ShouldBeDOCX()
    {
        // Act
        var format = _generator.OutputFormat;

        // Assert
        format.Should().Be("DOCX");
    }

    [Fact]
    public async Task GenerateAsync_WithValidData_ShouldCreateDocxFile()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new() { { "title", "Test Document" }, { "author", "John Doe" } }
        };

        var layout = new DocumentLayout
        {
            OutputFormat = "DOCX",
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
                    RenderedContent = "<h1>Test Document</h1><p>This is a test document by John Doe.</p>"
                }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
    public async Task GenerateAsync_WithHeaderAndFooter_ShouldIncludeThem()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new() { { "company", "Test Corp" } }
        };

        var layout = new DocumentLayout
        {
            OutputFormat = "DOCX",
            Sections = new List<DocumentSection>
            {
                new() { Name = "header", RenderedContent = "<div>Test Corp - Header</div>" },
                new() { Name = "content", RenderedContent = "<h1>Main Content</h1><p>Document body text here.</p>" },
                new() { Name = "footer", RenderedContent = "<div>Page footer</div>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
                new() { Name = "content", RenderedContent = "<p>Landscape DOCX document</p>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
                new() { Name = "content", RenderedContent = $"<p>DOCX with {pageSize} size</p>" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
    public async Task GenerateAsync_WithComplexHtml_ShouldHandleParagraphsAndLists()
    {
        // Arrange
        var data = new List<Dictionary<string, object>> { new() };
        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new()
                {
                    Name = "content",
                    RenderedContent = @"
                        <h1>Title</h1>
                        <p>Paragraph 1</p>
                        <p>Paragraph 2</p>
                        <ul>
                            <li>Item 1</li>
                            <li>Item 2</li>
                        </ul>
                    "
                }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
    public async Task GenerateAsync_WithEmptyContent_ShouldCreateEmptyDocument()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>();
        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new() { Name = "content", RenderedContent = "" }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

        try
        {
            // Act
            var (success, fileSize, _) = await _generator.GenerateAsync(data, layout, tempPath);

            // Assert
            success.Should().BeTrue();
            fileSize.Should().BeGreaterThan(0); // DOCX has minimum file size even when empty
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithMultipleDataRows_ShouldGenerateSuccessfully()
    {
        // Arrange
        var data = new List<Dictionary<string, object>>
        {
            new() { { "item", "Item 1" }, { "quantity", 10 } },
            new() { { "item", "Item 2" }, { "quantity", 20 } },
            new() { { "item", "Item 3" }, { "quantity", 30 } }
        };

        var layout = new DocumentLayout
        {
            Sections = new List<DocumentSection>
            {
                new()
                {
                    Name = "content",
                    RenderedContent = "<p>Order items: Item 1 (Qty: 10), Item 2 (Qty: 20), Item 3 (Qty: 30)</p>"
                }
            }
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.docx");

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
