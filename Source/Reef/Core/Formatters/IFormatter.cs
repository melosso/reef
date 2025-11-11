namespace Reef.Core.Formatters;

/// <summary>
/// Interface for data formatters
/// Implementations convert query results to various output formats
/// </summary>
public interface IFormatter
{
    /// <summary>
    /// Format data and write to output file
    /// </summary>
    /// <param name="data">Query results as list of dictionaries</param>
    /// <param name="outputPath">File path to write formatted output</param>
    /// <returns>Tuple containing success status, file size in bytes, and error message if failed</returns>
    Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> FormatAsync(
        List<Dictionary<string, object>> data, 
        string outputPath);
}