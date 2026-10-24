using System.Text.Json.Nodes;
using FluentAssertions;
using Reef.Core.Services;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Display;
using Xunit;

namespace Reef.Tests.Services;

public class LogTemplateTests
{
    [Fact]
    public void ProfilePrefixSurvivesMessagesThatNameTheProfileCode()
    {
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))!;
        var template = settings["Serilog"]!["WriteTo"]!.AsArray().First(s => s!["Name"]!.GetValue<string>() == "File")!["Args"]!["outputTemplate"]!.GetValue<string>();
        var output = new StringWriter();
        var formatter = new MessageTemplateTextFormatter(template);
        using var logger = new LoggerConfiguration().Enrich.FromLogContext()
            .WriteTo.Sink(new DelegatingSink(e => formatter.Format(e, output))).CreateLogger();

        using (LogContext.PushProperty(ExecutionLogContext.ProfileProperty, ExecutionLogContext.Prefix("ABC123")))
            logger.Information("No rows to export for profile {ProfileCode} ({ProfileName})", "ABC123", "Demo");

        output.ToString().Should().Contain("[INF] [ABC123] No rows to export for profile ABC123 (Demo)");
    }

    private sealed class DelegatingSink(Action<Serilog.Events.LogEvent> write) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => write(logEvent);
    }
}
