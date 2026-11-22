using System.Text.Json.Nodes;
using Reef.Core.Models;

namespace Reef.Core.Workflows.Conversion;

public static class JobSchedule
{
    // Job weekdays start Monday cron Sunday
    public static JsonObject? ToTrigger(Job job)
    {
        var at = job.StartTime ?? TimeSpan.Zero;
        var time = $"{at.Minutes} {at.Hours}";
        return job.ScheduleType switch
        {
            ScheduleType.Cron when !string.IsNullOrWhiteSpace(job.CronExpression) => Cron(job.CronExpression.Trim()),
            ScheduleType.Interval when job.IntervalMinutes > 0 => new JsonObject { ["Repeat"] = "Interval", ["EveryMinutes"] = job.IntervalMinutes },
            ScheduleType.Daily => Cron($"{time} * * *"),
            ScheduleType.Weekly when Days(job.WeekDays) is { Length: > 0 } days => Cron($"{time} * * {days}"),
            ScheduleType.Monthly when job.MonthDay is >= 1 and <= 31 => Cron($"{time} {job.MonthDay} * *"),
            _ => null
        };
    }

    private static JsonObject Cron(string expression) => new() { ["Repeat"] = "Cron", ["Cron"] = expression };

    private static string Days(string? weekDays) => string.Join(",", (weekDays ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(d => int.TryParse(d, out var day) && day is >= 0 and <= 6 ? (day + 1) % 7 : -1)
        .Where(d => d >= 0).Distinct().Order());
}
