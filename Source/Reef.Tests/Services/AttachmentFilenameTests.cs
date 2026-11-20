using FluentAssertions;
using Reef.Core.Services;
using Xunit;

namespace Reef.Tests.Services;

public class AttachmentFilenameTests
{
    [Theory]
    [InlineData("invoice-1.pdf", "invoice-1.pdf")]
    [InlineData("report.2026.11.xlsx", "report_2026_11.xlsx")]
    [InlineData("a<b>c.txt", "a_b_c.txt")]
    public void SanitizeFilenameKeepsTheExtension(string input, string expected) =>
        new AttachmentVariableResolver().SanitizeFilename(input).Should().Be(expected);

    [Fact]
    public void SanitizeFilenameRemovesPathTraversal() =>
        new AttachmentVariableResolver().SanitizeFilename("../../etc/passwd").Should().NotContainAny("/", "\\", "..");
}
