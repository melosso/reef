namespace Reef.Core.Exceptions;

public sealed class DuplicateNameException(string message) : InvalidOperationException(message);
