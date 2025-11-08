// Source/Reef/Core/Services/ValidationService.cs
// Service for validating SQL queries before execution

using Serilog;
using System.Text.RegularExpressions;

namespace Reef.Core.Services;

/// <summary>
/// Service for validating SQL queries for safety and correctness
/// </summary>
public class ValidationService
{
    /// <summary>
    /// Validate SQL query before execution
    /// </summary>
    public async Task<(bool IsValid, string? ErrorMessage, List<string> Warnings)> ValidateQueryAsync(
        string query, string connectionType)
    {
        return await Task.Run(() =>
        {
            var warnings = new List<string>();

            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(query))
                {
                    return (false, "Query cannot be empty", warnings);
                }

                // Normalize query for analysis
                var normalizedQuery = query.Trim().ToUpperInvariant();

                // Check for allowed statement types
                if (!StartsWithAllowedStatement(normalizedQuery))
                {
                    return (false, "Query must start with SELECT, WITH, or EXEC/EXECUTE", warnings);
                }

                // Check for dangerous operations
                CheckDangerousOperations(normalizedQuery, warnings);

                // Check for SQL injection patterns
                CheckSqlInjectionPatterns(query, warnings);

                // Connection type-specific validation
                ValidateConnectionTypeSpecific(query, connectionType, warnings);

                // Check for parameter placeholders
                ValidateParameterPlaceholders(query, connectionType, warnings);

                // Check for common syntax errors
                CheckCommonSyntaxErrors(query, warnings);

                // If we have critical errors, return as invalid
                var criticalWarnings = warnings.Where(w => w.StartsWith("CRITICAL:")).ToList();
                if (criticalWarnings.Any())
                {
                    return (false, criticalWarnings.First().Replace("CRITICAL: ", ""), warnings);
                }

                Log.Debug("Query validation passed with {WarningCount} warnings", warnings.Count);
                return (true, (string?)null, warnings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error validating query");
                return (false, $"Validation error: {ex.Message}", warnings);
            }
        });
    }

    /// <summary>
    /// Check if query starts with allowed statement
    /// </summary>
    private bool StartsWithAllowedStatement(string normalizedQuery)
    {
        var allowedStarts = new[] { "SELECT", "WITH", "EXEC", "EXECUTE" };
        return allowedStarts.Any(start => normalizedQuery.StartsWith(start));
    }

    /// <summary>
    /// Check for dangerous SQL operations
    /// </summary>
    private void CheckDangerousOperations(string normalizedQuery, List<string> warnings)
    {
        // Check for DROP statements
        if (Regex.IsMatch(normalizedQuery, @"\bDROP\s+(TABLE|DATABASE|SCHEMA|VIEW|INDEX|PROCEDURE|FUNCTION)", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains DROP statement which is not allowed");
        }

        // Check for TRUNCATE statements
        if (Regex.IsMatch(normalizedQuery, @"\bTRUNCATE\s+TABLE", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains TRUNCATE statement which is not allowed");
        }

        // Check for DELETE without WHERE
        if (Regex.IsMatch(normalizedQuery, @"\bDELETE\s+FROM\s+\w+(?!\s+WHERE)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: DELETE statement without WHERE clause detected - this will delete all rows");
        }

        // Check for UPDATE without WHERE
        if (Regex.IsMatch(normalizedQuery, @"\bUPDATE\s+\w+\s+SET\s+(?!.*WHERE)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: UPDATE statement without WHERE clause detected - this will update all rows");
        }

        // Check for ALTER statements
        if (Regex.IsMatch(normalizedQuery, @"\bALTER\s+(TABLE|DATABASE|SCHEMA|VIEW)", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains ALTER statement which is not allowed");
        }

        // Check for CREATE statements
        if (Regex.IsMatch(normalizedQuery, @"\bCREATE\s+(TABLE|DATABASE|SCHEMA|VIEW|INDEX|PROCEDURE|FUNCTION)", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains CREATE statement which is not allowed");
        }

        // Check for INSERT statements
        if (Regex.IsMatch(normalizedQuery, @"\bINSERT\s+INTO", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains INSERT statement which is not allowed");
        }

        // Check for GRANT/REVOKE statements
        if (Regex.IsMatch(normalizedQuery, @"\b(GRANT|REVOKE)\b", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query contains GRANT/REVOKE statement which is not allowed");
        }
    }

    /// <summary>
    /// Check for SQL injection patterns
    /// </summary>
    private void CheckSqlInjectionPatterns(string query, List<string> warnings)
    {
        // Check for comment-based injection attempts
        if (Regex.IsMatch(query, @"--|\*/|/\*", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: Query contains SQL comments which may indicate injection attempt");
        }

        // Check for stacked queries (semicolon followed by another statement)
        if (Regex.IsMatch(query, @";\s*(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: Query contains stacked statements which may indicate injection attempt");
        }

        // Check for UNION-based injection patterns
        if (Regex.IsMatch(query, @"UNION\s+(ALL\s+)?SELECT.*FROM", RegexOptions.IgnoreCase))
        {
            // This is actually valid in many cases, so just a warning
            warnings.Add("INFO: Query contains UNION SELECT - ensure this is intentional");
        }

        // Check for xp_cmdshell or other dangerous stored procedures
        if (Regex.IsMatch(query, @"\bxp_cmdshell\b|\bxp_regread\b|\bsp_oacreate\b", RegexOptions.IgnoreCase))
        {
            warnings.Add("CRITICAL: Query attempts to execute dangerous system stored procedures");
        }
    }

    /// <summary>
    /// Validate parameter placeholders
    /// </summary>
    private void ValidateParameterPlaceholders(string query, string connectionType, List<string> warnings)
    {
        // Check for parameter placeholders based on connection type
        switch (connectionType.ToLower())
        {
            case "sqlserver":
                // SQL Server uses @parameter
                if (Regex.IsMatch(query, @"@\w+"))
                {
                    warnings.Add("INFO: Query contains @parameters - ensure these are provided at execution");
                }
                break;

            case "mysql":
                // MySQL uses ? or :parameter
                if (Regex.IsMatch(query, @"\?|:\w+"))
                {
                    warnings.Add("INFO: Query contains parameters - ensure these are provided at execution");
                }
                break;

            case "postgresql":
                // PostgreSQL uses $1, $2, etc. or :parameter
                if (Regex.IsMatch(query, @"\$\d+|:\w+"))
                {
                    warnings.Add("INFO: Query contains parameters - ensure these are provided at execution");
                }
                break;
        }
    }

    /// <summary>
    /// Connection type-specific validation
    /// </summary>
    private void ValidateConnectionTypeSpecific(string query, string connectionType, List<string> warnings)
    {
        switch (connectionType.ToLower())
        {
            case "sqlserver":
                ValidateSqlServer(query, warnings);
                break;

            case "mysql":
                ValidateMySQL(query, warnings);
                break;

            case "postgresql":
                ValidatePostgreSQL(query, warnings);
                break;
        }
    }

    /// <summary>
    /// SQL Server-specific validation
    /// </summary>
    private void ValidateSqlServer(string query, List<string> warnings)
    {
        // Check for NOLOCK hint usage
        if (Regex.IsMatch(query, @"WITH\s*\(NOLOCK\)", RegexOptions.IgnoreCase))
        {
            warnings.Add("INFO: Query uses NOLOCK hint - be aware this allows dirty reads");
        }

        // Check for TOP without ORDER BY
        if (Regex.IsMatch(query, @"SELECT\s+TOP\s+\d+.*(?!ORDER\s+BY)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: TOP clause without ORDER BY may return unpredictable results");
        }

        // Check for deprecated syntax
        if (Regex.IsMatch(query, @"\*=|\+=", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: Query uses deprecated outer join syntax (*= or =*)");
        }
    }

    /// <summary>
    /// MySQL-specific validation
    /// </summary>
    private void ValidateMySQL(string query, List<string> warnings)
    {
        // Check for LIMIT without ORDER BY
        if (Regex.IsMatch(query, @"LIMIT\s+\d+(?!.*ORDER\s+BY)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: LIMIT clause without ORDER BY may return unpredictable results");
        }

        // Check for backtick usage (valid but noteworthy)
        if (query.Contains("`"))
        {
            warnings.Add("INFO: Query uses backticks for identifiers (MySQL-specific syntax)");
        }
    }

    /// <summary>
    /// PostgreSQL-specific validation
    /// </summary>
    private void ValidatePostgreSQL(string query, List<string> warnings)
    {
        // Check for LIMIT without ORDER BY
        if (Regex.IsMatch(query, @"LIMIT\s+\d+(?!.*ORDER\s+BY)", RegexOptions.IgnoreCase))
        {
            warnings.Add("WARNING: LIMIT clause without ORDER BY may return unpredictable results");
        }

        // Check for double quotes (PostgreSQL-specific)
        if (Regex.IsMatch(query, @"""[\w_]+"""))
        {
            warnings.Add("INFO: Query uses double quotes for identifiers (PostgreSQL-specific syntax)");
        }
    }

    /// <summary>
    /// Check for common syntax errors
    /// </summary>
    private void CheckCommonSyntaxErrors(string query, List<string> warnings)
    {
        // Check for unclosed quotes
        var singleQuotes = query.Count(c => c == '\'');
        if (singleQuotes % 2 != 0)
        {
            warnings.Add("CRITICAL: Query contains unclosed single quotes");
        }

        // Check for unclosed parentheses
        var openParens = query.Count(c => c == '(');
        var closeParens = query.Count(c => c == ')');
        if (openParens != closeParens)
        {
            warnings.Add("WARNING: Parentheses mismatch - may indicate syntax error");
        }

        // Check for trailing semicolon
        if (query.TrimEnd().EndsWith(";"))
        {
            warnings.Add("INFO: Query ends with semicolon - this is valid but may be unnecessary");
        }

        // Check for multiple spaces
        if (Regex.IsMatch(query, @"\s{3,}"))
        {
            warnings.Add("INFO: Query contains excessive whitespace - consider formatting");
        }
    }
}