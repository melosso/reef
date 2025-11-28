using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing profile groups with support for hierarchical tree structures
/// </summary>
public class GroupService
{
    private readonly string _connectionString;

    public GroupService(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Get all profile groups
    /// </summary>
    public async Task<List<ProfileGroup>> GetAllAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                SELECT * FROM ProfileGroups 
                ORDER BY ParentId NULLS FIRST, SortOrder, Name";
            
            var groups = await connection.QueryAsync<ProfileGroup>(sql);
            return groups.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving all profile groups");
            throw;
        }
    }

    /// <summary>
    /// Get profile group by ID
    /// </summary>
    public async Task<ProfileGroup?> GetByIdAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM ProfileGroups WHERE Id = @Id";
            
            var group = await connection.QueryFirstOrDefaultAsync<ProfileGroup>(sql, new { Id = id });
            return group;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profile group {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Get profile group by name
    /// </summary>
    public async Task<ProfileGroup?> GetByNameAsync(string name)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM ProfileGroups WHERE Name = @Name";
            
            var group = await connection.QueryFirstOrDefaultAsync<ProfileGroup>(sql, new { Name = name });
            return group;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profile group by name {Name}", name);
            throw;
        }
    }

    /// <summary>
    /// Get all child groups of a parent group
    /// </summary>
    public async Task<List<ProfileGroup>> GetByParentIdAsync(int? parentId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            string sql;
            
            if (parentId == null)
            {
                sql = "SELECT * FROM ProfileGroups WHERE ParentId IS NULL ORDER BY SortOrder, Name";
                var rootGroups = await connection.QueryAsync<ProfileGroup>(sql);
                return rootGroups.ToList();
            }
            else
            {
                sql = "SELECT * FROM ProfileGroups WHERE ParentId = @ParentId ORDER BY SortOrder, Name";
                var childGroups = await connection.QueryAsync<ProfileGroup>(sql, new { ParentId = parentId });
                return childGroups.ToList();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving child groups for parent {ParentId}", parentId);
            throw;
        }
    }

    /// <summary>
    /// Get hierarchical tree structure of all groups
    /// </summary>
    public async Task<List<GroupTreeNode>> GetTreeAsync()
    {
        try
        {
            var allGroups = await GetAllAsync();
            return BuildTree(allGroups, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error building group tree");
            throw;
        }
    }

    /// <summary>
    /// Recursively build hierarchical tree structure
    /// </summary>
    private List<GroupTreeNode> BuildTree(List<ProfileGroup> allGroups, int? parentId)
    {
        var result = new List<GroupTreeNode>();
        
        var children = allGroups
            .Where(g => g.ParentId == parentId)
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name);

        foreach (var group in children)
        {
            var node = new GroupTreeNode
            {
                Id = group.Id,
                Name = group.Name,
                ParentId = group.ParentId,
                Description = group.Description,
                SortOrder = group.SortOrder,
                ProfileCount = GetProfileCountForGroup(group.Id).Result,
                Children = BuildTree(allGroups, group.Id)
            };
            result.Add(node);
        }

        return result;
    }

    /// <summary>
    /// Get count of profiles in a group
    /// </summary>
    public async Task<int> GetProfileCountForGroup(int groupId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT COUNT(*) FROM Profiles WHERE GroupId = @GroupId";
            
            var count = await connection.ExecuteScalarAsync<int>(sql, new { GroupId = groupId });
            return count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting profile count for group {GroupId}", groupId);
            return 0;
        }
    }

    /// <summary>
    /// Create new profile group
    /// </summary>
    public async Task<int> CreateAsync(ProfileGroup group, string createdBy)
    {
        try
        {
            // Validate parent exists if specified
            if (group.ParentId.HasValue)
            {
                var parent = await GetByIdAsync(group.ParentId.Value);
                if (parent == null)
                {
                    throw new InvalidOperationException($"Parent group {group.ParentId} not found");
                }

                // Check for circular references
                if (await WouldCreateCircularReference(null, group.ParentId.Value))
                {
                    throw new InvalidOperationException("Cannot create group: would create circular reference");
                }
            }

            // Check for duplicate name
            var existing = await GetByNameAsync(group.Name);
            if (existing != null)
            {
                throw new InvalidOperationException($"Group with name '{group.Name}' already exists");
            }

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                INSERT INTO ProfileGroups (Name, ParentId, Description, SortOrder, CreatedAt, UpdatedAt)
                VALUES (@Name, @ParentId, @Description, @SortOrder, datetime('now'), datetime('now'));
                SELECT last_insert_rowid();";
            
            var id = await connection.ExecuteScalarAsync<int>(sql, new
            {
                group.Name,
                group.ParentId,
                group.Description,
                group.SortOrder
            });

            Log.Information("Created profile group {GroupId} ({Name}) by {User}", id, group.Name, createdBy);
            return id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating profile group {Name}", group.Name);
            throw;
        }
    }

    /// <summary>
    /// Update existing profile group
    /// </summary>
    public async Task<bool> UpdateAsync(ProfileGroup group)
    {
        try
        {
            // Validate parent exists if specified
            if (group.ParentId.HasValue)
            {
                if (group.ParentId.Value == group.Id)
                {
                    throw new InvalidOperationException("Group cannot be its own parent");
                }

                var parent = await GetByIdAsync(group.ParentId.Value);
                if (parent == null)
                {
                    throw new InvalidOperationException($"Parent group {group.ParentId} not found");
                }

                // Check for circular references
                if (await WouldCreateCircularReference(group.Id, group.ParentId.Value))
                {
                    throw new InvalidOperationException("Cannot update group: would create circular reference");
                }
            }

            // Check for duplicate name (excluding current group)
            var existing = await GetByNameAsync(group.Name);
            if (existing != null && existing.Id != group.Id)
            {
                throw new InvalidOperationException($"Group with name '{group.Name}' already exists");
            }

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                UPDATE ProfileGroups 
                SET Name = @Name,
                    ParentId = @ParentId,
                    Description = @Description,
                    SortOrder = @SortOrder,
                    UpdatedAt = datetime('now')
                WHERE Id = @Id";
            
            var rows = await connection.ExecuteAsync(sql, new
            {
                group.Id,
                group.Name,
                group.ParentId,
                group.Description,
                group.SortOrder
            });

            if (rows > 0)
            {
                Log.Information("Updated profile group {GroupId} ({Name})", group.Id, group.Name);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating profile group {GroupId}", group.Id);
            throw;
        }
    }

    /// <summary>
    /// Delete profile group
    /// Sets profiles' GroupId to NULL instead of cascade delete
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                // Check if group has children
                const string checkChildrenSql = "SELECT COUNT(*) FROM ProfileGroups WHERE ParentId = @Id";
                var childCount = await connection.ExecuteScalarAsync<int>(checkChildrenSql, new { Id = id }, transaction);
                
                if (childCount > 0)
                {
                    throw new InvalidOperationException($"Cannot delete group: it has {childCount} child group(s). Delete or move children first.");
                }

                // Set profiles' GroupId to NULL (don't cascade delete profiles)
                const string updateProfilesSql = "UPDATE Profiles SET GroupId = NULL WHERE GroupId = @Id";
                await connection.ExecuteAsync(updateProfilesSql, new { Id = id }, transaction);
                
                // Delete the group
                const string deleteGroupSql = "DELETE FROM ProfileGroups WHERE Id = @Id";
                var rows = await connection.ExecuteAsync(deleteGroupSql, new { Id = id }, transaction);
                
                transaction.Commit();
                
                if (rows > 0)
                {
                    Log.Information("Deleted profile group {GroupId}", id);
                }

                return rows > 0;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting profile group {GroupId}", id);
            throw;
        }
    }

    /// <summary>
    /// Move group to a new parent
    /// </summary>
    public async Task<bool> MoveToGroupAsync(int groupId, int? newParentId)
    {
        try
        {
            var group = await GetByIdAsync(groupId);
            if (group == null)
            {
                throw new InvalidOperationException($"Group {groupId} not found");
            }

            // Validate new parent
            if (newParentId.HasValue)
            {
                if (newParentId.Value == groupId)
                {
                    throw new InvalidOperationException("Group cannot be its own parent");
                }

                var newParent = await GetByIdAsync(newParentId.Value);
                if (newParent == null)
                {
                    throw new InvalidOperationException($"Parent group {newParentId} not found");
                }

                // Check for circular references
                if (await WouldCreateCircularReference(groupId, newParentId.Value))
                {
                    throw new InvalidOperationException("Cannot move group: would create circular reference");
                }
            }

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                UPDATE ProfileGroups 
                SET ParentId = @NewParentId,
                    UpdatedAt = datetime('now')
                WHERE Id = @GroupId";
            
            var rows = await connection.ExecuteAsync(sql, new
            {
                GroupId = groupId,
                NewParentId = newParentId
            });

            if (rows > 0)
            {
                Log.Information("Moved group {GroupId} to parent {NewParentId}", groupId, newParentId);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error moving group {GroupId} to parent {NewParentId}", groupId, newParentId);
            throw;
        }
    }

    /// <summary>
    /// Check if moving/creating group would create circular reference
    /// </summary>
    private async Task<bool> WouldCreateCircularReference(int? groupId, int newParentId)
    {
        try
        {
            var visited = new HashSet<int>();
            var currentId = newParentId;

            while (currentId != 0)
            {
                // If we've seen this ID before, we have a cycle
                if (!visited.Add(currentId))
                {
                    return true;
                }

                // If we reach the group we're trying to move, we have a cycle
                if (groupId.HasValue && currentId == groupId.Value)
                {
                    return true;
                }

                // Get parent of current group
                var current = await GetByIdAsync(currentId);
                if (current == null || !current.ParentId.HasValue)
                {
                    break;
                }

                currentId = current.ParentId.Value;
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking circular reference for group {GroupId} with parent {NewParentId}", groupId, newParentId);
            throw;
        }
    }

    /// <summary>
    /// Get profiles in a specific group
    /// </summary>
    public async Task<List<Profile>> GetProfilesInGroupAsync(int groupId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM Profiles WHERE GroupId = @GroupId ORDER BY Name";
            
            var profiles = await connection.QueryAsync<Profile>(sql, new { GroupId = groupId });
            return profiles.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profiles for group {GroupId}", groupId);
            throw;
        }
    }
}

/// <summary>
/// Tree node for hierarchical group display
/// </summary>
public class GroupTreeNode
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public int ProfileCount { get; set; }
    public List<GroupTreeNode> Children { get; set; } = new();
}