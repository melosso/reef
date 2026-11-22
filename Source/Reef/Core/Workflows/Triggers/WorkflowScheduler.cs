using NCrontab;
using Reef.Core.Workflows.Engine;
using Reef.Core.Workflows.Models;
using Serilog;

namespace Reef.Core.Workflows.Triggers;

public class WorkflowScheduler(WorkflowService workflows, WorkflowRunStore runs, WorkflowEngine engine)
{
    // shortcut runs due workflows one after another, move to a queue when runs overlap
    public async Task<int> RunDueAsync(DateTime now, CancellationToken ct)
    {
        var started = 0;
        foreach (var workflow in await workflows.ListAsync(ct))
        {
            if (!workflow.Active || workflow.Id is not { } id)
                continue;
            foreach (var trigger in workflow.Nodes.Where(n => n.Type == "schedule"))
            {
                var last = await runs.LastStartedAsync(id, trigger.Id, ct) ?? workflow.UpdatedAt ?? now;
                if (Next(trigger, last) is not { } next || next > now || await runs.IsRunningAsync(id, ct))
                    continue;
                try
                {
                    await engine.RunAsync(id, trigger.Id, ct: ct);
                    started++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Error(ex, "Scheduled workflow {WorkflowId} could not start", id);
                }
            }
        }
        return started;
    }

    public async Task<List<(WorkflowDocument Workflow, DateTime Next)>> UpcomingAsync(DateTime now, DateTime until, CancellationToken ct)
    {
        var upcoming = new List<(WorkflowDocument, DateTime)>();
        foreach (var workflow in (await workflows.ListAsync(ct)).Where(w => w.Active && w.Id is not null))
        {
            foreach (var trigger in workflow.Nodes.Where(n => n.Type == "schedule"))
            {
                var last = await runs.LastStartedAsync(workflow.Id!.Value, trigger.Id, ct) ?? workflow.UpdatedAt ?? now;
                if (Next(trigger, last) is { } next && next <= until)
                    upcoming.Add((workflow, next < now ? now : next));
            }
        }
        return upcoming;
    }

    private static DateTime? Next(WorkflowNode trigger, DateTime last)
    {
        var config = trigger.Config ?? new();
        if (NodeConfig.Text(config, "Repeat") == "Interval")
            return int.TryParse(NodeConfig.Text(config, "EveryMinutes"), out var minutes) && minutes > 0 ? last.AddMinutes(minutes) : null;
        return CrontabSchedule.TryParse(NodeConfig.Text(config, "Cron"))?.GetNextOccurrence(last);
    }
}
