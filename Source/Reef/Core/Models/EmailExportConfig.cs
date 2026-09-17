namespace Reef.Core.Models;

public class EmailExportConfig
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsEmailExport { get; set; }
    public string? EmailRecipientsColumn { get; set; }
    public string? EmailRecipientsHardcoded { get; set; }
    public bool UseHardcodedRecipients { get; set; }
    public string? EmailCcColumn { get; set; }
    public string? EmailCcHardcoded { get; set; }
    public bool UseHardcodedCc { get; set; }
    public string? EmailSubjectColumn { get; set; }
    public string? EmailSubjectHardcoded { get; set; }
    public bool UseHardcodedSubject { get; set; }
    public string? EmailAttachmentConfig { get; set; }
    public bool EmailGroupBySplitKey { get; set; }
    public string? SplitKeyColumn { get; set; }
    public string? DeltaSyncReefIdColumn { get; set; }
    public string? DeltaSyncReefIdNormalization { get; set; }

    public static EmailExportConfig FromProfile(Profile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        IsEmailExport = profile.IsEmailExport,
        EmailRecipientsColumn = profile.EmailRecipientsColumn,
        EmailRecipientsHardcoded = profile.EmailRecipientsHardcoded,
        UseHardcodedRecipients = profile.UseHardcodedRecipients,
        EmailCcColumn = profile.EmailCcColumn,
        EmailCcHardcoded = profile.EmailCcHardcoded,
        UseHardcodedCc = profile.UseHardcodedCc,
        EmailSubjectColumn = profile.EmailSubjectColumn,
        EmailSubjectHardcoded = profile.EmailSubjectHardcoded,
        UseHardcodedSubject = profile.UseHardcodedSubject,
        EmailAttachmentConfig = profile.EmailAttachmentConfig,
        EmailGroupBySplitKey = profile.EmailGroupBySplitKey,
        SplitKeyColumn = profile.SplitKeyColumn,
        DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
        DeltaSyncReefIdNormalization = profile.DeltaSyncReefIdNormalization
    };
}
