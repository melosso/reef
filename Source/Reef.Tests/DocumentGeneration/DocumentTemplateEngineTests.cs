using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Reef.Core.DocumentGeneration;
using Reef.Core.TemplateEngines;
using Xunit;

namespace Reef.Tests.DocumentGeneration;

public class DocumentTemplateEngineTests : IDisposable
{
    private readonly Mock<ITemplateEngine> _mockScribanEngine;
    private readonly Mock<IDocumentGeneratorFactory> _mockGeneratorFactory;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly DocumentTemplateEngine _engine;
    private readonly string _testExportsPath;

    public DocumentTemplateEngineTests()
    {
        _mockScribanEngine = new Mock<ITemplateEngine>();
        _mockGeneratorFactory = new Mock<IDocumentGeneratorFactory>();
        _mockConfiguration = new Mock<IConfiguration>();

        _testExportsPath = Path.Combine(Path.GetTempPath(), $"reef_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testExportsPath);

        _mockConfiguration.Setup(c => c["ExportsPath"]).Returns(_testExportsPath);

        _engine = new DocumentTemplateEngine(
            _mockScribanEngine.Object,
            _mockGeneratorFactory.Object,
            _mockConfiguration.Object);
    }

    [Fact]
    public async Task TransformAsync_WithValidPdfTemplate_ShouldGeneratePdf()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{! pageSize: A4 }}
{{! orientation: Portrait }}

{{# content }}
<h1>Invoice {{ .[0].invoice_number }}</h1>
{{/ content }}
";

        var data = new List<Dictionary<string, object>>
        {
            new() { { "invoice_number", "INV-001" } }
        };

        var mockGenerator = new Mock<IDocumentGenerator>();
        mockGenerator.Setup(g => g.OutputFormat).Returns("PDF");
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<DocumentLayout>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync((true, 1024L, (string?)null));

        _mockGeneratorFactory.Setup(f => f.GetGenerator("PDF"))
            .Returns(mockGenerator.Object);

        _mockScribanEngine.Setup(e => e.TransformAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync("<h1>Invoice INV-001</h1>");

        // Act
        var result = await _engine.TransformAsync(data, template);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().EndWith(".pdf");
        _mockGeneratorFactory.Verify(f => f.GetGenerator("PDF"), Times.Once);
        mockGenerator.Verify(g => g.GenerateAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.IsAny<DocumentLayout>(),
            It.IsAny<string>(),
            null), Times.Once);
    }

    [Fact]
    public async Task TransformAsync_WithDocxFormat_ShouldGenerateDocx()
    {
        // Arrange
        var template = @"
{{! format: docx }}
{{# content }}
<p>Document content</p>
{{/ content }}
";

        var data = new List<Dictionary<string, object>> { new() };

        var mockGenerator = new Mock<IDocumentGenerator>();
        mockGenerator.Setup(g => g.OutputFormat).Returns("DOCX");
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<DocumentLayout>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync((true, 2048L, (string?)null));

        _mockGeneratorFactory.Setup(f => f.GetGenerator("DOCX"))
            .Returns(mockGenerator.Object);

        _mockScribanEngine.Setup(e => e.TransformAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync("<p>Document content</p>");

        // Act
        var result = await _engine.TransformAsync(data, template);

        // Assert
        result.Should().EndWith(".docx");
        _mockGeneratorFactory.Verify(f => f.GetGenerator("DOCX"), Times.Once);
    }

    [Fact]
    public void ValidateTemplate_WithValidTemplate_ShouldReturnTrue()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{! pageSize: A4 }}
{{! orientation: Portrait }}

{{# content }}
<h1>Valid Template</h1>
{{/ content }}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((true, (string?)null));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("XYZ")] // Invalid format
    [InlineData("xml")] // Wrong case, should be uppercase
    public void ValidateTemplate_WithInvalidFormat_ShouldReturnFalse(string format)
    {
        // Arrange
        var template = $@"
{{{{! format: {format} }}}}
{{{{# content }}}}
<p>Test</p>
{{{{/ content }}}}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((true, (string?)null));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("Invalid output format");
    }

    [Fact]
    public void ValidateTemplate_WithInvalidPageSize_ShouldReturnFalse()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{! pageSize: B5 }}
{{# content }}
<p>Test</p>
{{/ content }}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((true, (string?)null));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("Invalid page size");
    }

    [Fact]
    public void ValidateTemplate_WithInvalidOrientation_ShouldReturnFalse()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{! orientation: Vertical }}
{{# content }}
<p>Test</p>
{{/ content }}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((true, (string?)null));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("Invalid orientation");
    }

    [Fact]
    public void ValidateTemplate_WithNoSections_ShouldReturnFalse()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
";

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("must contain at least one section");
    }

    [Fact]
    public void ValidateTemplate_WithInvalidScribanSyntax_ShouldReturnFalse()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{# content }}
{{ unclosed_tag
{{/ content }}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((false, "Unclosed tag"));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errorMessage.Should().Contain("Scriban syntax error");
    }

    [Fact]
    public async Task TransformAsync_WithHeaderFooterAndContent_ShouldRenderAllSections()
    {
        // Arrange
        var template = @"
{{! format: pdf }}

{{# header }}
Header: {{ company_name }}
{{/ header }}

{{# content }}
Content here
{{/ content }}

{{# footer }}
Footer text
{{/ footer }}
";

        var data = new List<Dictionary<string, object>>
        {
            new() { { "company_name", "Test Corp" } }
        };

        var mockGenerator = new Mock<IDocumentGenerator>();
        mockGenerator.Setup(g => g.OutputFormat).Returns("PDF");
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.Is<DocumentLayout>(l => l.Sections.Count == 3),
                It.IsAny<string>(),
                null))
            .ReturnsAsync((true, 1024L, (string?)null));

        _mockGeneratorFactory.Setup(f => f.GetGenerator("PDF"))
            .Returns(mockGenerator.Object);

        _mockScribanEngine.Setup(e => e.TransformAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync((List<Dictionary<string, object>> d, string t, Dictionary<string, object>? c) => t);

        // Act
        await _engine.TransformAsync(data, template);

        // Assert
        mockGenerator.Verify(g => g.GenerateAsync(
            It.IsAny<List<Dictionary<string, object>>>(),
            It.Is<DocumentLayout>(l => l.Sections.Count == 3),
            It.IsAny<string>(),
            null), Times.Once);
    }

    [Fact]
    public void ValidateTemplate_WithAllValidDirectives_ShouldParseCorrectly()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{! pageSize: Letter }}
{{! orientation: Landscape }}
{{! marginTop: 30 }}
{{! marginBottom: 30 }}
{{! marginLeft: 25 }}
{{! marginRight: 25 }}

{{# content }}
<p>Test content</p>
{{/ content }}
";

        _mockScribanEngine.Setup(e => e.ValidateTemplate(It.IsAny<string>()))
            .Returns((true, (string?)null));

        // Act
        var (isValid, errorMessage) = _engine.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Fact]
    public async Task TransformAsync_WhenGenerationFails_ShouldThrowException()
    {
        // Arrange
        var template = @"
{{! format: pdf }}
{{# content }}
Test
{{/ content }}
";

        var mockGenerator = new Mock<IDocumentGenerator>();
        mockGenerator.Setup(g => g.OutputFormat).Returns("PDF");
        mockGenerator.Setup(g => g.GenerateAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<DocumentLayout>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync((false, 0L, "Generation failed"));

        _mockGeneratorFactory.Setup(f => f.GetGenerator("PDF"))
            .Returns(mockGenerator.Object);

        _mockScribanEngine.Setup(e => e.TransformAsync(
                It.IsAny<List<Dictionary<string, object>>>(),
                It.IsAny<string>(),
                null))
            .ReturnsAsync("Test");

        var data = new List<Dictionary<string, object>> { new() };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _engine.TransformAsync(data, template));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testExportsPath))
        {
            Directory.Delete(_testExportsPath, true);
        }
    }
}
