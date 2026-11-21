using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class PrivilegeBoundaryTests(ReefApiFactory factory)
{
    private const string Password = "Secret-Pass-123";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, string Id)> CreateUserAsync(string role = "User")
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = $"user{Guid.NewGuid():N}"[..16];
        var created = await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = Password, role }, _ct);
        created.EnsureSuccessStatusCode();
        var users = await admin.GetFromJsonAsync<JsonArray>("/api/admin/users", _ct);
        var id = users!.First(u => u!["username"]!.GetValue<string>() == name)!["id"]!.GetValue<string>();

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = Password }, _ct);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, id);
    }

    [Fact]
    public async Task UsersCannotPointDestinationsAtTheFileSystem()
    {
        var (user, _) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        object Destination() => new { name = $"Disk {Guid.NewGuid():N}", type = "Local", configurationJson = JsonSerializer.Serialize(new { BasePath = factory.DataFolder }), isActive = true, tags = "", description = "" };

        (await user.PostAsJsonAsync("/api/destinations", Destination(), _ct)).IsSuccessStatusCode.Should().BeFalse();
        (await admin.PostAsJsonAsync("/api/destinations", Destination(), _ct)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Theory]
    [InlineData("orders/{workflow}_{guid}.{format}", "Success")]
    [InlineData("../escape-{guid}.csv", "Failed")]
    [InlineData("/tmp/escape-{guid}.csv", "Failed")]
    public async Task FileNameTemplatesCannotLeaveTheExportFolder(string template, string status)
    {
        var admin = await factory.CreateAdminClientAsync();
        var connectionId = await EmailProfiles.IdOfAsync(await admin.PostAsJsonAsync("/api/connections",
            new { name = $"Names {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={Path.Combine(factory.DataFolder, $"names-{Guid.NewGuid():N}.db")}", isActive = true, hash = "" }, _ct));
        var destinations = await admin.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        var workflowId = await TestWorkflows.CreateChainAsync(admin,
            TestWorkflows.Node("query", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT 1 AS One" }),
            TestWorkflows.Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV", ["FileName"] = template }));

        var run = await TestWorkflows.RunAsync(admin, workflowId);

        run["status"]!.GetValue<string>().Should().Be(status, run.ToJsonString());
    }

    private static JsonObject Workflow(string type, JsonObject config) => new()
    {
        ["name"] = $"Privileged {Guid.NewGuid():N}",
        ["nodes"] = new JsonArray(
            new JsonObject { ["id"] = "start", ["type"] = "manual", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject() },
            new JsonObject { ["id"] = "step", ["type"] = type, ["x"] = 240, ["y"] = 0, ["config"] = config }),
        ["edges"] = new JsonArray(new JsonObject { ["from"] = "start", ["to"] = "step" })
    };

    public static TheoryData<string, string> PrivilegedSteps => new()
    {
        { "command", """{"Runs":"Script","Interpreter":"sh","Script":"id"}""" },
        { "fetch", """{"From":"Local","Path":"/etc"}""" },
        { "fetch", """{"From":"Sftp","Settings":{"host":"files"},"Archive":true,"ArchiveFolder":"/srv"}""" },
        { "write", """{"To":"LocalFile","FilePath":"/tmp/out.csv"}""" }
    };

    [Theory]
    [MemberData(nameof(PrivilegedSteps))]
    public async Task UsersCannotAddPrivilegedWorkflowStepsButAdminsCan(string type, string config)
    {
        var (user, _) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();

        (await user.PostAsJsonAsync("/api/workflows", Workflow(type, JsonNode.Parse(config)!.AsObject()), _ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/workflows", Workflow(type, JsonNode.Parse(config)!.AsObject()), _ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UsersCanRenameAWorkflowButNotChangeItsScript()
    {
        var (user, _) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/workflows", Workflow("command", new JsonObject { ["Runs"] = "Script", ["Interpreter"] = "sh", ["Script"] = "id" }), _ct);
        var workflow = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
        var id = workflow["id"]!.GetValue<string>();

        workflow["name"] = workflow["name"]!.GetValue<string>() + " renamed";
        (await user.PutAsJsonAsync($"/api/workflows/{id}", workflow, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        workflow["nodes"]![1]!["config"]!["Script"] = "cat /etc/passwd";
        (await user.PutAsJsonAsync($"/api/workflows/{id}", workflow, _ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivatedUsersLoseApiAccessAndCannotRefresh()
    {
        var (user, id) = await CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        (await user.GetAsync("/api/workflows", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { role = "User", isActive = false }, _ct)).EnsureSuccessStatusCode();

        (await user.GetAsync("/api/workflows", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
            cmd.CommandText = $"UPDATE Users SET Role = 'User' WHERE Ulid = '{id}'";
            await cmd.ExecuteNonQueryAsync(_ct);
        }

        (await former.GetAsync("/api/admin/users", _ct)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task ChangingThePasswordSignsOutExistingTokens()
    {
        var (user, _) = await CreateUserAsync();
        (await user.GetAsync("/api/workflows", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await user.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = "Another-Pass-456" }, _ct)).EnsureSuccessStatusCode();

        (await user.GetAsync("/api/workflows", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("PUT", "/api/admin/users/{self}")]
    [InlineData("POST", "/api/admin/danger/reset-database")]
    [InlineData("GET", "/api/admin/interpreters")]
    public async Task UsersAreRefusedBeforeTheRequestIsRead(string method, string path)
    {
        var (user, id) = await CreateUserAsync();

        var response = await user.SendAsync(new HttpRequestMessage(new HttpMethod(method), path.Replace("{self}", id)) { Content = JsonContent.Create(new { }) }, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
