using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class PrivilegeBoundaryTests(ReefApiFactory factory)
{
    private const string Password = "Secret-Pass-123";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, int Id)> CreateUserAsync(string role = "User")
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = $"user{Guid.NewGuid():N}"[..16];
        var created = await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = Password, role }, _ct);
        created.EnsureSuccessStatusCode();
        var users = await admin.GetFromJsonAsync<JsonArray>("/api/admin/users", _ct);
        var id = users!.First(u => u!["username"]!.GetValue<string>() == name)!["id"]!.GetValue<int>();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = Password }, _ct);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, id);
    }

    private static JsonObject Node(string type, JsonObject config) => new() { ["id"] = type, ["type"] = type, ["x"] = 40, ["y"] = 40, ["config"] = config };

    private async Task<JsonObject> ExportDocumentAsync(HttpClient client, JsonObject destinationConfig, JsonObject? postprocess = null)
    {
        var admin = await factory.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/connections",
            new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={Path.Combine(factory.DataFolder, "shop.db")}", isActive = true, hash = "" }, _ct);
        created.EnsureSuccessStatusCode();
        var connectionId = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        destinationConfig["OutputDestinationId"] = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<int>();
        destinationConfig["OutputFormat"] = "CSV";
        var nodes = new JsonArray(
            Node("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT 1 AS One" }),
            Node("destination", destinationConfig));
        if (postprocess is not null) nodes.Add(Node("postprocess", postprocess));
        return new JsonObject { ["kind"] = "export", ["settings"] = new JsonObject { ["Name"] = $"Export {Guid.NewGuid():N}", ["IsEnabled"] = false }, ["nodes"] = nodes };
    }

    private static JsonObject ScriptStep() => new()
    {
        ["PostProcessType"] = "Script",
        ["PostProcessConfig"] = new JsonObject { ["Interpreter"] = "sh", ["ScriptIsInline"] = true, ["ScriptPathOrInline"] = "id" }
    };

    [Fact]
    public async Task UsersCannotRunAdHocScripts()
    {
        var (user, _) = await CreateUserAsync();

        (await user.PostAsJsonAsync("/api/profiles/test-script", new { interpreter = "sh", scriptPathOrInline = "id", scriptIsInline = true }, _ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.PostAsJsonAsync("/api/profiles/interpreters/sh/test", new { }, _ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UsersCannotAddScriptStepsButAdminsCan()
    {
        var (user, _) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();

        (await user.PostAsJsonAsync("/api/profile-documents/export", await ExportDocumentAsync(user, new JsonObject(), ScriptStep()), _ct))
            .IsSuccessStatusCode.Should().BeFalse();
        (await admin.PostAsJsonAsync("/api/profile-documents/export", await ExportDocumentAsync(admin, new JsonObject(), ScriptStep()), _ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UsersCannotReadServerFilesThroughImportPreview()
    {
        var (user, _) = await CreateUserAsync();
        var settings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        var preview = await user.PostAsJsonAsync("/api/import-profiles/preview",
            new { name = "peek", sourceType = "Local", sourceFilePath = settings, sourceFileSelection = "Exact", sourceFormat = "JSON", targetTable = "x", maxRows = 10 }, _ct);

        preview.IsSuccessStatusCode.Should().BeFalse();
        (await preview.Content.ReadAsStringAsync(_ct)).Should().NotContain("SecretKey");
    }

    [Fact]
    public async Task UsersCannotPointDestinationsAtTheFileSystem()
    {
        var (user, _) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        object Destination() => new { name = $"Disk {Guid.NewGuid():N}", type = "Local", configurationJson = "{\"BasePath\":\"/tmp\"}", isActive = true, tags = "", description = "" };

        (await user.PostAsJsonAsync("/api/destinations", Destination(), _ct)).IsSuccessStatusCode.Should().BeFalse();
        (await admin.PostAsJsonAsync("/api/destinations", Destination(), _ct)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Theory]
    [InlineData("orders/{profile}_{guid}.{format}", true)]
    [InlineData("../escape-{guid}.csv", false)]
    [InlineData("/tmp/escape-{guid}.csv", false)]
    public async Task FileNameTemplatesCannotLeaveTheExportFolder(string template, bool accepted)
    {
        var admin = await factory.CreateAdminClientAsync();

        var saved = await admin.PostAsJsonAsync("/api/profile-documents/export", await ExportDocumentAsync(admin, new JsonObject { ["FilenameTemplate"] = template }), _ct);

        saved.IsSuccessStatusCode.Should().Be(accepted, await saved.Content.ReadAsStringAsync(_ct));
    }

    [Fact]
    public async Task DeactivatedUsersLoseApiAccessAndCannotRefresh()
    {
        var (user, id) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        (await user.GetAsync("/api/groups", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { role = "User", isActive = false }, _ct)).EnsureSuccessStatusCode();

        (await user.GetAsync("/api/groups", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await user.PostAsync("/api/auth/refresh", null, _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DemotedAdminsLoseAdminAccess()
    {
        var (former, id) = await CreateUserAsync("Admin");
        (await former.GetAsync("/api/admin/users", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The API blocks admins from editing other admins. The test demotes through the database.
        await using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = $"UPDATE Users SET Role = 'User' WHERE Id = {id}";
            await cmd.ExecuteNonQueryAsync(_ct);
        }

        (await former.GetAsync("/api/admin/users", _ct)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task ChangingThePasswordSignsOutExistingTokens()
    {
        var (user, _) = await CreateUserAsync();
        (await user.GetAsync("/api/groups", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await user.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "Another-Pass-456" }, _ct)).EnsureSuccessStatusCode();

        (await user.GetAsync("/api/groups", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("PUT", "/api/admin/users/1")]
    [InlineData("POST", "/api/admin/danger/reset-database")]
    [InlineData("GET", "/api/profiles/interpreters")]
    public async Task UsersAreRefusedBeforeTheRequestIsRead(string method, string path)
    {
        var (user, _) = await CreateUserAsync();

        var response = await user.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) }, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
