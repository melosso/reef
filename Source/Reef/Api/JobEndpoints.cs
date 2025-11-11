using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Api;

public static class JobsEndpoints
{
    public static void MapJobsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/jobs")
            .RequireAuthorization()
            .WithTags("Jobs");

        // GET: List all jobs
        group.MapGet("/", async (
            [FromServices] JobService service,
            [FromQuery] bool enabledOnly = false) =>
        {
            var jobs = await service.GetAllAsync(enabledOnly);
            return Results.Ok(jobs);
        })
        .WithName("GetAllJobs")
        .Produces<IEnumerable<Job>>(200);

        // GET: Get job by ID
        group.MapGet("/{id:int}", async (
            int id,
            [FromServices] JobService service) =>
        {
            var job = await service.GetByIdAsync(id);
            return job != null
                ? Results.Ok(job)
                : Results.NotFound(new { message = "Job not found" });
        })
        .WithName("GetJobById")
        .Produces<Job>(200)
        .Produces(404);

        // POST: Create new job
        group.MapPost("/", async (
            [FromBody] Job job,
            [FromServices] JobService service) =>
        {
            try
            {
                var created = await service.CreateAsync(job);
                return Results.Created($"/api/jobs/{created.Id}", created);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("CreateJob")
        .Produces<Job>(201)
        .Produces(400);

        // PUT: Update job
        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] Job job,
            [FromServices] JobService service) =>
        {
            if (id != job.Id)
            {
                return Results.BadRequest(new { message = "ID mismatch" });
            }

            try
            {
                var success = await service.UpdateAsync(job);
                return success
                    ? Results.Ok(job)
                    : Results.NotFound(new { message = "Job not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("UpdateJob")
        .Produces<Job>(200)
        .Produces(400)
        .Produces(404);

        // DELETE: Delete job
        group.MapDelete("/{id:int}", async (
            int id,
            [FromServices] JobService service) =>
        {
            try
            {
                var success = await service.DeleteAsync(id);
                return success
                    ? Results.NoContent()
                    : Results.NotFound(new { message = "Job not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("DeleteJob")
        .Produces(204)
        .Produces(404);

        // POST: Trigger job manually
        group.MapPost("/{id:int}/trigger", async (
            int id,
            [FromBody] JobTriggerRequest? request,
            [FromServices] JobService service,
            [FromServices] JobExecutorService executor) =>
        {
            try
            {
                var job = await service.GetByIdAsync(id);
                if (job == null)
                {
                    return Results.NotFound(new { message = "Job not found" });
                }

                var executionId = await executor.TriggerJobAsync(
                    job,
                    request?.IgnoreDependencies ?? false,
                    request?.Parameters);

                return Results.Accepted($"/api/jobs/{id}/executions/{executionId}",
                    new { executionId, message = "Job triggered successfully" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("TriggerJob")
        .Produces(202)
        .Produces(400)
        .Produces(404);

        // POST: Control job (start, stop, pause, resume)
        group.MapPost("/{id:int}/control", async (
            int id,
            [FromBody] JobControlRequest request,
            [FromServices] JobService service) =>
        {
            try
            {
                var job = await service.GetByIdAsync(id);
                if (job == null)
                {
                    return Results.NotFound(new { message = "Job not found" });
                }

                var success = request.Action switch
                {
                    JobControlAction.Start => await service.UpdateStatusAsync(id, JobStatus.Scheduled),
                    JobControlAction.Stop => await service.UpdateStatusAsync(id, JobStatus.Idle),
                    JobControlAction.Cancel => await service.UpdateStatusAsync(id, JobStatus.Cancelled),
                    _ => false
                };

                return success
                    ? Results.Ok(new { message = $"Job {request.Action.ToString().ToLower()} successful" })
                    : Results.BadRequest(new { message = "Control action failed" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ControlJob")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        // GET: Get job execution history
        group.MapGet("/{id:int}/executions", async (
            int id,
            [FromServices] JobService service,
            [FromQuery] int limit = 50) =>
        {
            var executions = await service.GetExecutionHistoryAsync(id, limit);
            return Results.Ok(executions);
        })
        .WithName("GetJobExecutionHistory")
        .Produces<IEnumerable<JobExecution>>(200);

        // GET: Get latest execution
        group.MapGet("/{id:int}/executions/latest", async (
            int id,
            [FromServices] JobService service) =>
        {
            var execution = await service.GetLatestExecutionAsync(id);
            return execution != null
                ? Results.Ok(execution)
                : Results.NotFound(new { message = "No executions found" });
        })
        .WithName("GetLatestJobExecution")
        .Produces<JobExecution>(200)
        .Produces(404);

        // GET: Get jobs by status
        group.MapGet("/status/{status}", async (
            JobStatus status,
            [FromServices] JobService service) =>
        {
            var jobs = await service.GetJobsByStatusAsync(status);
            return Results.Ok(jobs);
        })
        .WithName("GetJobsByStatus")
        .Produces<IEnumerable<Job>>(200);

        // GET: Get jobs by profile
        group.MapGet("/profile/{profileId:int}", async (
            int profileId,
            [FromServices] JobService service) =>
        {
            var jobs = await service.GetByProfileIdAsync(profileId);
            return Results.Ok(jobs);
        })
        .WithName("GetJobsByProfile")
        .Produces<IEnumerable<Job>>(200);

        // GET: Get due jobs
        group.MapGet("/due", async (
            [FromServices] JobService service) =>
        {
            var jobs = await service.GetDueJobsAsync();
            return Results.Ok(jobs);
        })
        .WithName("GetDueJobs")
        .Produces<IEnumerable<Job>>(200);

        // GET: Get job status summary
        group.MapGet("/summary/status", async (
            [FromServices] JobService service) =>
        {
            var summary = await service.GetJobStatusSummaryAsync();
            return Results.Ok(summary);
        })
        .WithName("GetJobStatusSummary")
        .Produces<Dictionary<JobStatus, int>>(200);

        // GET: Get job types
        group.MapGet("/types", () =>
        {
            var types = Enum.GetValues<JobType>()
                .Select(t => new
                {
                    value = (int)t,
                    name = t.ToString(),
                    description = GetJobTypeDescription(t)
                });
            return Results.Ok(types);
        })
        .WithName("GetJobTypes")
        .Produces(200);

        // GET: Get schedule types
        group.MapGet("/schedule-types", () =>
        {
            var types = Enum.GetValues<ScheduleType>()
                .Select(t => new
                {
                    value = (int)t,
                    name = t.ToString(),
                    description = GetScheduleTypeDescription(t)
                });
            return Results.Ok(types);
        })
        .WithName("GetScheduleTypes")
        .Produces(200);

        // POST: Calculate next run time
        group.MapPost("/calculate-next-run", (
            [FromBody] Job job,
            [FromServices] JobService service) =>
        {
            var nextRun = service.CalculateNextRunTime(job);
            return Results.Ok(new { nextRunTime = nextRun });
        })
        .WithName("CalculateNextRunTime")
        .Produces(200);

        // GET: Get queue metrics (detailed)
        group.MapGet("/queue/metrics", async (
            [FromServices] JobScheduler scheduler) =>
        {
            try
            {
                var metrics = await scheduler.GetQueueMetricsAsync();
                return Results.Ok(metrics);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get queue metrics: {ex.Message}");
            }
        })
        .WithName("GetQueueMetrics")
        .Produces<JobQueueMetrics>(200)
        .Produces(500);

        // GET: Get queue status (simple health check)
        group.MapGet("/queue/status", async (
            [FromServices] JobScheduler scheduler) =>
        {
            try
            {
                var metrics = await scheduler.GetQueueMetricsAsync();
                return Results.Ok(new 
                { 
                    healthy = metrics.QueueDepth < 50,
                    queueDepth = metrics.QueueDepth,
                    availableSlots = metrics.AvailableSlots,
                    activeJobs = metrics.ActiveJobs,
                    timestamp = metrics.Timestamp
                });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to get queue status: {ex.Message}");
            }
        })
        .WithName("GetQueueStatus")
        .Produces(200)
        .Produces(500);

        // POST: Resume an auto-paused job (circuit breaker recovery)
        group.MapPost("/{id:int}/resume", async (
            int id,
            [FromServices] JobService service) =>
        {
            try
            {
                var job = await service.GetByIdAsync(id);
                if (job == null)
                {
                    return Results.NotFound(new { message = "Job not found" });
                }

                // Reset circuit breaker state
                await service.ResumeCircuitBreakerJobAsync(id);

                return Results.Ok(new {
                    success = true,
                    message = "Job resumed successfully"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("ResumeJob")
        .Produces(200)
        .Produces(400)
        .Produces(404);

        // POST: Acknowledge failures and reset counter
        group.MapPost("/{id:int}/acknowledge-failures", async (
            int id,
            [FromServices] JobService service) =>
        {
            try
            {
                var job = await service.GetByIdAsync(id);
                if (job == null)
                {
                    return Results.NotFound(new { message = "Job not found" });
                }

                // Reset failure counter
                await service.ResetFailureCounterAsync(id);

                // Remove "circuit-breaker" tag if present
                if (!string.IsNullOrWhiteSpace(job.Tags))
                {
                    var tags = job.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => !string.Equals(t, "circuit-breaker", StringComparison.OrdinalIgnoreCase));
                    job.Tags = string.Join(",", tags);
                }

                // Recalculate and update next run time
                job.NextRunTime = service.CalculateNextRunTime(job);

                // Always persist the job so UI and API are in sync
                await service.UpdateAsync(job);

                return Results.Ok(new {
                    success = true,
                    message = "Failure counter reset"
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("AcknowledgeFailures")
        .Produces(200)
        .Produces(400)
        .Produces(404);

    }

    private static string GetJobTypeDescription(JobType type)
    {
        return type switch
        {
            JobType.ProfileExecution => "Execute a profile query and export",
            JobType.DataTransfer => "Transfer files between destinations",
            JobType.Cleanup => "Delete old files and clean up",
            JobType.HealthCheck => "System health check",
            JobType.BackupDatabase => "Backup Reef.db database",
            JobType.CustomScript => "Execute custom script",
            JobType.ApiCall => "Call external API",
            JobType.EmailReport => "Send email report",
            JobType.FileArchive => "Archive old exports",
            JobType.DataValidation => "Validate data integrity",
            _ => type.ToString()
        };
    }

    private static string GetScheduleTypeDescription(ScheduleType type)
    {
        return type switch
        {
            ScheduleType.Manual => "Manual execution only",
            ScheduleType.Cron => "Cron expression",
            ScheduleType.Interval => "Fixed interval",
            ScheduleType.Daily => "Daily at specific time",
            ScheduleType.Weekly => "Weekly on specific days",
            ScheduleType.Monthly => "Monthly on specific day",
            ScheduleType.OnDependency => "Run after dependencies complete",
            ScheduleType.Webhook => "Triggered by webhook",
            ScheduleType.FileWatcher => "Triggered by file system event",
            _ => type.ToString()
        };
    }
}