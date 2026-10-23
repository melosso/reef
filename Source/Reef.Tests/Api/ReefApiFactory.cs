using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reef.Tests.Api;

// Boots Reef against a throwaway data folder through the short environment variable names
public class ReefApiFactory : WebApplicationFactory<Reef.Program>
{
    public string DataFolder { get; } = Directory.CreateTempSubdirectory("reef-api").FullName;

    public ReefApiFactory()
    {
        Environment.SetEnvironmentVariable("DATABASE_PATH", Path.Combine(DataFolder, "Reef.db"));
        Environment.SetEnvironmentVariable("ENCRYPTION_KEY", "ReefApiTestsEncryptionKey0123456789");
        Environment.SetEnvironmentVariable("LISTEN_PORT", FreePort().ToString());
    }

    // Main refuses to start when its configured port is taken.
    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    public const string AdminPassword = "Admin-Pass-123";

    private Task<(string Username, bool ChangeRequired)>? _admin;

    // The seeded admin account, signed in once with the one-time password from the log.
    public Task<(string Username, bool ChangeRequired)> SeededAdminAsync() => _admin ??= SignInSeededAdminAsync();

    private async Task<(string, bool)> SignInSeededAdminAsync()
    {
        CreateClient();
        var log = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "log"), "reef-*.log")
            .Select(f => { using var r = new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)); return r.ReadToEnd(); });
        var seeded = Regex.Matches(string.Concat(log), @"Created the first admin account (\S+) with one-time password (\S+)").Last();
        var (username, password) = (seeded.Groups[1].Value, seeded.Groups[2].Value);

        var client = await SignInAsync(username, password);
        var check = await client.PostAsJsonAsync("/api/auth/validate", new { token = client.DefaultRequestHeaders.Authorization!.Parameter });
        var changeRequired = (await check.Content.ReadFromJsonAsync<JsonObject>())!["passwordChangeRequired"]!.GetValue<bool>();
        (await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = password, newPassword = AdminPassword })).EnsureSuccessStatusCode();
        return (username, changeRequired);
    }

    public async Task<HttpClient> CreateAdminClientAsync() => await SignInAsync((await SeededAdminAsync()).Username, AdminPassword);

    private async Task<HttpClient> SignInAsync(string username, string password)
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>())!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            Directory.Delete(DataFolder, recursive: true);
    }
}
