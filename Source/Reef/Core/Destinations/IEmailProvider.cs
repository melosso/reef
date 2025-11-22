using MimeKit;

namespace Reef.Core.Destinations;

/// <summary>
/// Interface for email provider implementations
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Send email message through the provider
    /// </summary>
    /// <param name="message">The MIME message to send</param>
    /// <param name="config">Email destination configuration</param>
    /// <returns>Tuple of (success, messageId/finalPath, errorMessage)</returns>
    Task<(bool Success, string? FinalPath, string? ErrorMessage)> SendEmailAsync(
        MimeMessage message,
        EmailDestinationConfiguration config);
}
