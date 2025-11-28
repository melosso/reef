// Source/Reef/Core/Services/DependencyResolver.cs
// Service for managing and resolving profile dependencies

using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing profile dependencies and execution order
/// Prevents circular dependencies and validates execution chains
/// </summary>
public class DependencyResolver
{
    private readonly string _connectionString;

    public DependencyResolver(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Get all dependencies for a profile
    /// </summary>
    public async Task<List<ProfileDependency>> GetDependenciesAsync(int profileId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                SELECT * FROM ProfileDependencies 
                WHERE ProfileId = @ProfileId 
                ORDER BY ExecutionOrder";

            var dependencies = await connection.QueryAsync<ProfileDependency>(sql, new { ProfileId = profileId });
            return dependencies.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving dependencies for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Get all profiles that depend on this profile
    /// </summary>
    public async Task<List<int>> GetDependentProfilesAsync(int profileId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                SELECT ProfileId FROM ProfileDependencies 
                WHERE DependsOnProfileId = @ProfileId";

            var dependents = await connection.QueryAsync<int>(sql, new { ProfileId = profileId });
            return dependents.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving dependent profiles for {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Add a dependency between profiles
    /// </summary>
    public async Task<int> AddDependencyAsync(int profileId, int dependsOnProfileId, int executionOrder = 0)
    {
        try
        {
            // Validate profiles exist
            await using var connection = new SqliteConnection(_connectionString);
            
            var profileExists = await connection.ExecuteScalarAsync<bool>(
                "SELECT COUNT(*) FROM Profiles WHERE Id = @Id", new { Id = profileId });
            var dependsOnExists = await connection.ExecuteScalarAsync<bool>(
                "SELECT COUNT(*) FROM Profiles WHERE Id = @Id", new { Id = dependsOnProfileId });

            if (!profileExists)
            {
                throw new InvalidOperationException($"Profile {profileId} not found");
            }

            if (!dependsOnExists)
            {
                throw new InvalidOperationException($"Dependency profile {dependsOnProfileId} not found");
            }

            // Prevent self-dependency
            if (profileId == dependsOnProfileId)
            {
                throw new InvalidOperationException("Profile cannot depend on itself");
            }

            // Check for circular dependencies
            if (await WouldCreateCircularDependency(profileId, dependsOnProfileId))
            {
                throw new InvalidOperationException(
                    $"Adding this dependency would create a circular reference between profiles {profileId} and {dependsOnProfileId}");
            }

            // Check if dependency already exists
            var existing = await connection.ExecuteScalarAsync<int?>(
                "SELECT Id FROM ProfileDependencies WHERE ProfileId = @ProfileId AND DependsOnProfileId = @DependsOnProfileId",
                new { ProfileId = profileId, DependsOnProfileId = dependsOnProfileId });

            if (existing.HasValue)
            {
                throw new InvalidOperationException("Dependency already exists");
            }

            // Add dependency
            const string sql = @"
                INSERT INTO ProfileDependencies (ProfileId, DependsOnProfileId, ExecutionOrder, CreatedAt)
                VALUES (@ProfileId, @DependsOnProfileId, @ExecutionOrder, datetime('now'));
                SELECT last_insert_rowid();";

            var dependencyId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                ProfileId = profileId,
                DependsOnProfileId = dependsOnProfileId,
                ExecutionOrder = executionOrder
            });

            Log.Information("Added dependency: Profile {ProfileId} depends on {DependsOnProfileId}", 
                profileId, dependsOnProfileId);

            return dependencyId;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error adding dependency from {ProfileId} to {DependsOnProfileId}", 
                profileId, dependsOnProfileId);
            throw;
        }
    }

    /// <summary>
    /// Remove a dependency
    /// </summary>
    public async Task<bool> RemoveDependencyAsync(int dependencyId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "DELETE FROM ProfileDependencies WHERE Id = @Id";

            var rows = await connection.ExecuteAsync(sql, new { Id = dependencyId });

            if (rows > 0)
            {
                Log.Information("Removed dependency {DependencyId}", dependencyId);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error removing dependency {DependencyId}", dependencyId);
            throw;
        }
    }

    /// <summary>
    /// Validate all dependencies for a profile
    /// </summary>
    public async Task<(bool IsValid, List<string> Errors)> ValidateDependenciesAsync(int profileId)
    {
        var errors = new List<string>();

        try
        {
            var dependencies = await GetDependenciesAsync(profileId);

            foreach (var dependency in dependencies)
            {
                // Check if dependency profile exists
                await using var connection = new SqliteConnection(_connectionString);
                var dependencyExists = await connection.ExecuteScalarAsync<bool>(
                    "SELECT COUNT(*) FROM Profiles WHERE Id = @Id", 
                    new { Id = dependency.DependsOnProfileId });

                if (!dependencyExists)
                {
                    errors.Add($"Dependency profile {dependency.DependsOnProfileId} not found");
                }

                // Check if dependency profile is enabled
                var dependencyEnabled = await connection.ExecuteScalarAsync<bool>(
                    "SELECT IsEnabled FROM Profiles WHERE Id = @Id", 
                    new { Id = dependency.DependsOnProfileId });

                if (!dependencyEnabled)
                {
                    errors.Add($"Dependency profile {dependency.DependsOnProfileId} is disabled");
                }

                // Check for circular dependencies
                if (await WouldCreateCircularDependency(profileId, dependency.DependsOnProfileId))
                {
                    errors.Add($"Circular dependency detected with profile {dependency.DependsOnProfileId}");
                }
            }

            return (errors.Count == 0, errors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating dependencies for profile {ProfileId}", profileId);
            errors.Add($"Validation error: {ex.Message}");
            return (false, errors);
        }
    }

    /// <summary>
    /// Check if all dependency profiles have completed successfully
    /// </summary>
    public async Task<(bool AllCompleted, List<int> PendingDependencies)> CheckDependenciesCompletedAsync(int profileId)
    {
        try
        {
            var dependencies = await GetDependenciesAsync(profileId);
            var pendingDependencies = new List<int>();

            await using var connection = new SqliteConnection(_connectionString);

            foreach (var dependency in dependencies)
            {
                // Check if dependency profile has a recent successful execution
                const string sql = @"
                    SELECT COUNT(*) FROM ProfileExecutions 
                    WHERE ProfileId = @ProfileId 
                    AND Status = 'Success' 
                    AND CompletedAt >= datetime('now', '-1 hour')";

                var hasRecentSuccess = await connection.ExecuteScalarAsync<int>(sql, 
                    new { ProfileId = dependency.DependsOnProfileId }) > 0;

                if (!hasRecentSuccess)
                {
                    pendingDependencies.Add(dependency.DependsOnProfileId);
                    Log.Warning("Profile {ProfileId} dependency {DependencyId} has not completed successfully recently", 
                        profileId, dependency.DependsOnProfileId);
                }
            }

            return (pendingDependencies.Count == 0, pendingDependencies);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking dependencies for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Get dependency tree/graph for visualization
    /// </summary>
    public async Task<DependencyGraph> GetDependencyGraphAsync(int profileId)
    {
        try
        {
            var graph = new DependencyGraph
            {
                ProfileId = profileId,
                Dependencies = new List<DependencyNode>()
            };

            var visited = new HashSet<int>();
            await BuildDependencyGraphRecursive(profileId, graph.Dependencies, visited, 0);

            return graph;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error building dependency graph for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Get execution order for a profile and its dependencies
    /// </summary>
    public async Task<List<int>> GetExecutionOrderAsync(int profileId)
    {
        try
        {
            var executionOrder = new List<int>();
            var visited = new HashSet<int>();
            await BuildExecutionOrderRecursive(profileId, executionOrder, visited);
            return executionOrder;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error building execution order for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Check if adding a dependency would create a circular reference
    /// </summary>
    private async Task<bool> WouldCreateCircularDependency(int profileId, int dependsOnProfileId)
    {
        try
        {
            var visited = new HashSet<int> { profileId };
            return await HasCircularDependencyRecursive(dependsOnProfileId, profileId, visited);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking circular dependency between {ProfileId} and {DependsOnProfileId}", 
                profileId, dependsOnProfileId);
            throw;
        }
    }

    /// <summary>
    /// Recursively check for circular dependencies
    /// </summary>
    private async Task<bool> HasCircularDependencyRecursive(int currentProfileId, int targetProfileId, HashSet<int> visited)
    {
        // If we've reached the target profile, we have a cycle
        if (currentProfileId == targetProfileId)
        {
            return true;
        }

        // If we've already visited this profile, no cycle from this path
        if (visited.Contains(currentProfileId))
        {
            return false;
        }

        visited.Add(currentProfileId);

        // Get dependencies of current profile
        var dependencies = await GetDependenciesAsync(currentProfileId);

        // Recursively check each dependency
        foreach (var dependency in dependencies)
        {
            if (await HasCircularDependencyRecursive(dependency.DependsOnProfileId, targetProfileId, visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively build dependency graph for visualization
    /// </summary>
    private async Task BuildDependencyGraphRecursive(int profileId, List<DependencyNode> nodes, HashSet<int> visited, int depth)
    {
        if (visited.Contains(profileId) || depth > 10) // Prevent infinite recursion
        {
            return;
        }

        visited.Add(profileId);

        await using var connection = new SqliteConnection(_connectionString);
        var profile = await connection.QueryFirstOrDefaultAsync<Profile>(
            "SELECT * FROM Profiles WHERE Id = @Id", new { Id = profileId });

        if (profile == null)
        {
            return;
        }

        var node = new DependencyNode
        {
            ProfileId = profileId,
            ProfileName = profile.Name,
            Depth = depth,
            Children = new List<DependencyNode>()
        };

        var dependencies = await GetDependenciesAsync(profileId);
        foreach (var dependency in dependencies)
        {
            await BuildDependencyGraphRecursive(dependency.DependsOnProfileId, node.Children, visited, depth + 1);
        }

        nodes.Add(node);
    }

    /// <summary>
    /// Recursively build execution order (topological sort)
    /// </summary>
    private async Task BuildExecutionOrderRecursive(int profileId, List<int> executionOrder, HashSet<int> visited)
    {
        if (visited.Contains(profileId))
        {
            return;
        }

        visited.Add(profileId);

        // First, add all dependencies
        var dependencies = await GetDependenciesAsync(profileId);
        foreach (var dependency in dependencies.OrderBy(d => d.ExecutionOrder))
        {
            await BuildExecutionOrderRecursive(dependency.DependsOnProfileId, executionOrder, visited);
        }

        // Then add the profile itself
        executionOrder.Add(profileId);
    }
}

/// <summary>
/// Dependency graph for visualization
/// </summary>
public class DependencyGraph
{
    public int ProfileId { get; set; }
    public List<DependencyNode> Dependencies { get; set; } = new();
}

/// <summary>
/// Node in dependency graph
/// </summary>
public class DependencyNode
{
    public int ProfileId { get; set; }
    public string ProfileName { get; set; } = "";
    public int Depth { get; set; }
    public List<DependencyNode> Children { get; set; } = new();
}