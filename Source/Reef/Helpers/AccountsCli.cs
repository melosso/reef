using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;

namespace Reef.Helpers;

/// <summary>
/// `reef accounts` recovers user accounts from the shell
/// </summary>
public static class AccountsCli
{
    private static readonly PasswordHasher Hasher = new();

    /// <summary>
    /// What to type to run this build, which differs between a published apphost and `dotnet Reef.dll`
    /// </summary>
    public static string Invocation()
    {
        var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe)) return "reef";
        if (dll.Length == 0 || string.Equals(Path.GetFileNameWithoutExtension(exe),
                                             Path.GetFileNameWithoutExtension(dll), StringComparison.OrdinalIgnoreCase))
            return Quote(exe);
        return $"dotnet {Quote(dll)}";
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Reef")
            ?? $"Data Source={configuration["Reef:DatabasePath"] ?? "Reef.db"}";
        var databasePath = Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource);

        // refuses to run without the database so sqlite cannot create an empty one
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"No Reef database at \"{databasePath}\".");
            Console.Error.WriteLine("Set Reef__DatabasePath or run this next to the Reef executable.");
            return 1;
        }

        await using var conn = new SqliteConnection(connectionString);
        try
        {
            await conn.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open \"{databasePath}\": {ex.Message}");
            return 1;
        }

        var rest = args.Skip(1).ToArray();
        return rest switch
        {
            ["list"] => await ListAsync(conn),
            ["password", var user, var password] => await SetPasswordAsync(conn, user, password),
            ["create", var user, var password] => await CreateAsync(conn, user, password, "Admin"),
            ["create", var user, var password, var role] => await CreateAsync(conn, user, password, role),
            ["promote", var user] => await SetRoleAsync(conn, user, "Admin"),
            ["demote", var user] => await SetRoleAsync(conn, user, "User"),
            ["enable", var user] => await SetActiveAsync(conn, user, true),
            ["disable", var user] => await SetActiveAsync(conn, user, false),
            ["mfa-reset", var user] => await ResetMfaAsync(conn, user),
            ["delete", var user] => await DeleteAsync(conn, user),
            _ => Usage(rest.Length == 0 ? 0 : 1),
        };
    }

    private static async Task<User?> FindAsync(SqliteConnection conn, string username)
    {
        var matches = (await conn.QueryAsync<User>(
            "SELECT * FROM Users WHERE (LOWER(Username) = @Name OR LOWER(Email) = @Name) AND IsDeleted = 0",
            new { Name = username.ToLowerInvariant() })).ToList();

        var account = matches.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            ?? (matches.Count == 1 ? matches[0] : null);
        if (account is null)
            Console.Error.WriteLine(matches.Count > 1 ? $"\"{username}\" matches more than one account. Use the username." : $"No account named \"{username}\".");
        return account;
    }

    private static async Task<int> ListAsync(SqliteConnection conn)
    {
        var accounts = (await conn.QueryAsync<User>("SELECT * FROM Users WHERE IsDeleted = 0 ORDER BY Username")).ToList();
        if (accounts.Count == 0)
        {
            Console.WriteLine("No accounts.");
            Console.WriteLine($"Create one with:  {Invocation()} accounts create <username> <password>");
            return 0;
        }

        Console.WriteLine($"{"USERNAME",-24} {"EMAIL",-32} {"ROLE",-8} {"STATE",-10} {"MFA",-5} {"LAST SIGN-IN",-20}");
        foreach (var a in accounts)
        {
            var last = a.LastLoginAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never";
            Console.WriteLine($"{a.Username,-24} {a.Email ?? "",-32} {a.Role,-8} {(a.IsActive ? "active" : "inactive"),-10} {(a.MfaEnabled ? "on" : "off"),-5} {last,-20}");
        }
        return 0;
    }

    private static string? ValidatePassword(string password) =>
        password.Length < 6 ? "Password must be at least 6 characters long." : null;

    private static async Task<int> SetPasswordAsync(SqliteConnection conn, string username, string password)
    {
        if (await FindAsync(conn, username) is not { } account) return 1;

        if (ValidatePassword(password) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }

        await conn.ExecuteAsync("UPDATE Users SET PasswordHash = @Hash, PasswordChangeRequired = 0 WHERE Id = @Id",
            new { Hash = Hasher.HashPassword(password), account.Id });

        Console.WriteLine($"Password set for {account.Username}.");
        return 0;
    }

    private static async Task<int> CreateAsync(SqliteConnection conn, string username, string password, string role)
    {
        var name = username.Trim().ToLowerInvariant();
        if (name.Length < 3)
        {
            Console.Error.WriteLine("Username must be at least 3 characters long.");
            return 1;
        }
        if (ValidatePassword(password) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }
        role = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? "Admin"
            : string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ? "User" : role;
        if (role is not ("Admin" or "User"))
        {
            Console.Error.WriteLine($"Unknown role \"{role}\". Use admin or user.");
            return 1;
        }
        if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE LOWER(Username) = @Name AND IsDeleted = 0", new { Name = name }) > 0)
        {
            Console.Error.WriteLine($"\"{name}\" is already taken.");
            return 1;
        }

        await conn.ExecuteAsync(
            @"INSERT INTO Users (Username, PasswordHash, Role, IsActive, CreatedAt, PasswordChangeRequired)
              VALUES (@Name, @Hash, @Role, 1, @CreatedAt, 0)",
            new { Name = name, Hash = Hasher.HashPassword(password), Role = role, CreatedAt = DateTime.UtcNow });

        Console.WriteLine($"Created {name} as {role}.");
        return 0;
    }

    private static async Task<int> SetRoleAsync(SqliteConnection conn, string username, string role)
    {
        if (await FindAsync(conn, username) is not { } account) return 1;

        if (account.Role == role)
        {
            Console.WriteLine($"{account.Username} is already {role}.");
            return 0;
        }
        if (role != "Admin" && await IsLastAdministratorAsync(conn, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Promote another account first.");
            return 1;
        }

        await conn.ExecuteAsync("UPDATE Users SET Role = @Role WHERE Id = @Id", new { Role = role, account.Id });

        Console.WriteLine($"{account.Username} is now {role}.");
        return 0;
    }

    private static async Task<int> SetActiveAsync(SqliteConnection conn, string username, bool active)
    {
        if (await FindAsync(conn, username) is not { } account) return 1;

        if (account.IsActive == active)
        {
            Console.WriteLine($"{account.Username} is already {(active ? "active" : "inactive")}.");
            return 0;
        }
        if (!active && await IsLastAdministratorAsync(conn, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Promote another account first.");
            return 1;
        }

        await conn.ExecuteAsync("UPDATE Users SET IsActive = @Active WHERE Id = @Id", new { Active = active, account.Id });

        Console.WriteLine($"{account.Username} is now {(active ? "active" : "inactive")}.");
        return 0;
    }

    private static async Task<int> ResetMfaAsync(SqliteConnection conn, string username)
    {
        if (await FindAsync(conn, username) is not { } account) return 1;

        await conn.ExecuteAsync(
            "UPDATE Users SET MfaEnabled = 0, MfaMethod = NULL, TotpSecret = NULL, PendingTotpSecret = NULL, BackupCodes = NULL WHERE Id = @Id",
            new { account.Id });

        Console.WriteLine($"Two-factor authentication removed for {account.Username}.");
        return 0;
    }

    private static async Task<int> DeleteAsync(SqliteConnection conn, string username)
    {
        if (await FindAsync(conn, username) is not { } account) return 1;

        if (await IsLastAdministratorAsync(conn, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Create another one first.");
            return 1;
        }

        await conn.ExecuteAsync(
            "UPDATE Users SET IsDeleted = 1, DeletedAt = @DeletedAt, DeletedBy = 'cli', IsActive = 0 WHERE Id = @Id",
            new { DeletedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), account.Id });

        Console.WriteLine($"Deleted {account.Username}.");
        return 0;
    }

    private static async Task<bool> IsLastAdministratorAsync(SqliteConnection conn, User account)
    {
        if (account.Role != "Admin" || !account.IsActive) return false;
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE Id <> @Id AND IsActive = 1 AND IsDeleted = 0 AND Role = 'Admin'",
            new { account.Id }) == 0;
    }

    private static int Usage(int exitCode)
    {
        var me = Invocation();
        Console.WriteLine("accounts commands:");
        Console.WriteLine($"  {me} accounts list");
        Console.WriteLine($"  {me} accounts create <username> <password> [admin|user]");
        Console.WriteLine($"  {me} accounts password <username|email> <password>");
        Console.WriteLine($"  {me} accounts promote <username|email>");
        Console.WriteLine($"  {me} accounts demote <username|email>");
        Console.WriteLine($"  {me} accounts enable <username|email>");
        Console.WriteLine($"  {me} accounts disable <username|email>");
        Console.WriteLine($"  {me} accounts mfa-reset <username|email>");
        Console.WriteLine($"  {me} accounts delete <username|email>");
        Console.WriteLine();
        Console.WriteLine("Reads Reef:DatabasePath from appsettings.json or Reef__DatabasePath.");
        return exitCode;
    }
}
