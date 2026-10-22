using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Reef.Tests.Api;

// Boots Reef against a throwaway data folder. Main reads its configuration from environment variables.
public class ReefApiFactory : WebApplicationFactory<Reef.Program>
{
    public string DataFolder { get; } = Directory.CreateTempSubdirectory("reef-api").FullName;

    public ReefApiFactory()
    {
        Environment.SetEnvironmentVariable("Reef__DatabasePath", Path.Combine(DataFolder, "Reef.db"));
        Environment.SetEnvironmentVariable("REEF_ENCRYPTION_KEY", "ReefApiTestsEncryptionKey0123456789");
        Environment.SetEnvironmentVariable("Reef__ListenPort", FreePort().ToString());
    }

    // Main refuses to start when its configured port is taken.
    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin@reef.local", password = "admin123" });
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
