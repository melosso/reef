namespace Reef.Core.DocumentGeneration;

/// <summary>
/// Factory for creating document generators based on output format
/// </summary>
public interface IDocumentGeneratorFactory
{
    /// <summary>
    /// Get the appropriate document generator for the specified output format
    /// </summary>
    /// <param name="outputFormat">Output format (PDF, DOCX, ODT)</param>
    /// <returns>Document generator instance</returns>
    IDocumentGenerator GetGenerator(string outputFormat);
}

/// <summary>
/// Default implementation of document generator factory
/// </summary>
public class DocumentGeneratorFactory : IDocumentGeneratorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DocumentGeneratorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IDocumentGenerator GetGenerator(string outputFormat)
    {
        return outputFormat.ToUpperInvariant() switch
        {
            "PDF" => _serviceProvider.GetRequiredService<PdfGenerator>(),
            "DOCX" => _serviceProvider.GetRequiredService<DocxGenerator>(),
            "ODT" => throw new NotImplementedException("ODT generator not yet implemented (deferred to future phase)"),
            _ => throw new ArgumentException($"Unsupported document format: {outputFormat}. Supported formats: PDF, DOCX")
        };
    }
}
