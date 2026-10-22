using FluentAssertions;
using Reef.Core.Services;
using Xunit;

namespace Reef.Tests.Services;

public class ValidationServiceTests
{
    private readonly ValidationService _service = new();

    [Theory]
    [InlineData("UPDATE Orders SET Status = 'Exported' WHERE OrderId <= 3")]
    [InlineData("INSERT INTO ExportLog (RunId) VALUES (1)")]
    [InlineData("DELETE FROM Staging WHERE Id = 1")]
    [InlineData("TRUNCATE TABLE Staging")]
    public async Task Processing_commands_may_write(string command)
    {
        var (isValid, error, _) = await _service.ValidateCommandAsync(command, "Sqlite");
        isValid.Should().BeTrue(error);
    }

    [Fact]
    public async Task Processing_commands_may_not_drop_objects()
    {
        var (isValid, _, _) = await _service.ValidateCommandAsync("DROP TABLE Orders", "Sqlite");
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task Export_queries_stay_read_only()
    {
        var (isValid, _, _) = await _service.ValidateQueryAsync("UPDATE Orders SET Status = 'x' WHERE OrderId = 1", "Sqlite");
        isValid.Should().BeFalse();
    }
}
