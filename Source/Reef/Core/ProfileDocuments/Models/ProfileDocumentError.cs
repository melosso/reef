namespace Reef.Core.ProfileDocuments.Models;

public record ProfileDocumentError(string Message, string? NodeId = null, string? Field = null);
