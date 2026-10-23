using System.Text.Json.Nodes;
using Reef.Core.Exceptions;
using Reef.Core.Models;

namespace Reef.Core.Security;

/// <summary>
/// Restricts script steps and file system paths to administrators. Both run as the Reef service account.
/// </summary>
public sealed class PrivilegedSettingsGuard(IHttpContextAccessor accessor)
{
    private static readonly string[] FileSystemDestinations = ["Local", "NetworkShare"];

    public void Check(Profile profile, Profile? stored) =>
        Require("Script steps", Scripts(profile.PreProcessType, profile.PreProcessConfig, profile.PostProcessType, profile.PostProcessConfig),
            stored is null ? null : Scripts(stored.PreProcessType, stored.PreProcessConfig, stored.PostProcessType, stored.PostProcessConfig));

    public void Check(ImportProfile profile, ImportProfile? stored)
    {
        Require("Script steps", Scripts(profile.PreProcessType, profile.PreProcessConfig, profile.PostProcessType, profile.PostProcessConfig),
            stored is null ? null : Scripts(stored.PreProcessType, stored.PreProcessConfig, stored.PostProcessType, stored.PostProcessConfig));
        Require("Local file paths", Paths(profile), stored is null ? null : Paths(stored));
    }

    public void Check(string type, string? configurationJson, string? storedType, string? storedConfigurationJson) =>
        Require("File system destinations", FileSystem(type, configurationJson), storedType is null ? null : FileSystem(storedType, storedConfigurationJson));

    public void RequireAdministrator(string what) => Require(what, what, null);

    private void Require(string what, string? requested, string? current)
    {
        if (requested is null || requested == current || IsTrusted()) return;
        throw new PrivilegedSettingsException($"{what} can only be changed by an administrator.");
    }

    // Background work such as the scheduler has no request.
    private bool IsTrusted() => accessor.HttpContext is not { } context || context.User.IsInRole("Admin");

    private static string? Scripts(string? preType, string? preConfig, string? postType, string? postConfig) =>
        preType == "Script" || postType == "Script"
            ? $"{(preType == "Script" ? Normalize(preConfig) : "")}|{(postType == "Script" ? Normalize(postConfig) : "")}"
            : null;

    private static string? Paths(ImportProfile p)
    {
        var archive = p.ArchiveAfterImport ? p.ArchivePath : null;
        var target = p.TargetType == "LocalFile" ? p.LocalTargetPath : null;
        return string.IsNullOrWhiteSpace(p.SourceFilePath) && string.IsNullOrWhiteSpace(archive) && string.IsNullOrWhiteSpace(target)
            ? null
            : $"{p.SourceFilePath}|{archive}|{target}";
    }

    private static string? FileSystem(string type, string? configurationJson) =>
        FileSystemDestinations.Contains(type, StringComparer.OrdinalIgnoreCase) ? $"{type}|{Normalize(configurationJson)}" : null;

    private static string Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            return JsonNode.Parse(json)?.ToJsonString() ?? "";
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }
}
