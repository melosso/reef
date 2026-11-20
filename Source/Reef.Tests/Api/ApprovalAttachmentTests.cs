using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Reef.Core.Models;
using Reef.Core.Services;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class ApprovalAttachmentTests(ReefApiFactory factory)
{
    [Fact]
    public async Task HeldEmailStoresBinaryAttachmentFromItsRow()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmailExportService>();
        var content = new byte[] { 1, 2, 3, 4 };
        var rows = new List<Dictionary<string, object>> { new() { ["Email"] = "a@example.com", ["Doc"] = content, ["Fname"] = "invoice-1.bin" } };
        var profile = new EmailExportConfig { Id = 1, Name = "Invoices", IsEmailExport = true, EmailRecipientsColumn = "Email", UseHardcodedSubject = true, EmailSubjectHardcoded = "Invoice" };
        var template = new QueryTemplate { Name = "Mail", Type = QueryTemplateType.ScribanTemplate, Template = "<p>{{ rows[0].Email }}</p>" };
        var attachments = new AttachmentConfig { Enabled = true, Mode = "Binary", Binary = new BinaryAttachmentMode { ContentColumnName = "Doc", FilenameColumnName = "Fname" } };

        var (rendered, errors) = await service.RenderEmailsForApprovalAsync(profile, template, rows, attachments);

        errors.Should().BeEmpty();
        var stored = JsonSerializer.Deserialize<AttachmentConfig>(rendered.Single().AttachmentConfigJson!)!;
        stored.GeneratedAttachments!.Select(a => $"{a.Filename}:{a.ContentBase64}").Should().Equal($"invoice-1.bin:{Convert.ToBase64String(content)}");
    }
}
