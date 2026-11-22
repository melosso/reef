using Serilog;

namespace Reef.Core.Workflows.Triggers;

public class WorkflowSchedulerService(IServiceScopeFactory scopes, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Clamp(configuration.GetValue("Reef:Scheduler:CheckIntervalSeconds", 60), 10, 3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowScheduler>().RunDueAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "Workflow scheduler failed");
            }
        }
    }
}
