using Reef.Core.Nodes.Models;

namespace Reef.Core.Nodes.Exceptions;

public class NodeValidationException(IReadOnlyList<NodeError> errors)
    : Exception(errors.Count > 0 ? errors[0].Message : "Invalid settings")
{
    public IReadOnlyList<NodeError> Errors { get; } = errors;
}
