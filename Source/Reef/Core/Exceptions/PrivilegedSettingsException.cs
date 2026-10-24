namespace Reef.Core.Exceptions;

public sealed class PrivilegedSettingsException(string message) : UnauthorizedAccessException(message);
