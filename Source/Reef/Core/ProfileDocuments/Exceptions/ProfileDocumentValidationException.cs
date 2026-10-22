using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments.Exceptions;

public class ProfileDocumentValidationException(IReadOnlyList<ProfileDocumentError> errors)
    : Exception(errors.Count > 0 ? errors[0].Message : "Invalid profile document")
{
    public IReadOnlyList<ProfileDocumentError> Errors { get; } = errors;
}
