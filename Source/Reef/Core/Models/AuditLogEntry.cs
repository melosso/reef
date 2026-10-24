namespace Reef.Core.Models;

/// <summary>
/// Audit log row with the code and name of the profile it refers to
/// </summary>
public class AuditLogEntry : AuditLog
{
    public string? EntityCode { get; set; }
    public string? EntityName { get; set; }
}
