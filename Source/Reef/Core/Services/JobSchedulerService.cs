using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Reef.Core.Models;
using Serilog;
using System.Collections.Concurrent;

namespace Reef.Core.Services;

/// <summary>
/// Production-ready job scheduler with priority queue and bounded concurrency
/// Handles job execution, concurrency control, and error recovery
/// 
/// Architecture:
/// - Producer loop: Polls database for due jobs and enqueues them
/// - Consumer workers: Dequeue and execute jobs with priority-based ordering
/// - Concurrency control: Bounded by MaxConcurrentJobs setting from configuration
/// </summary>
public class JobScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly JobQueue _queue;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _executionLocks = new();
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _runningJobs = new();
    private readonly int _checkIntervalSeconds;
    private readonly int _maxConcurrentJobs;
    private readonly int _workerCount;
    
    public JobScheduler(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        
        // Read configuration values with sensible defaults
        _checkIntervalSeconds = configuration.GetValue<int>("Reef:Jobs:CheckIntervalSeconds", 10);
        _maxConcurrentJobs = configuration.GetValue<int>("Reef:Jobs:MaxConcurrentJobs", 10);
        
        // Validate configuration
        if (_checkIntervalSeconds < 5 || _checkIntervalSeconds > 300)
        {
            Log.Warning("Reef:Jobs:CheckIntervalSeconds ({Value}) out of recommended range (5-300 seconds), using default: 10", _checkIntervalSeconds);
            _checkIntervalSeconds = 10;
        }
        
        if (_maxConcurrentJobs < 1 || _maxConcurrentJobs > 100)
        {
            Log.Warning("Reef:Jobs:MaxConcurrentJobs ({Value}) out of recommended range (1-100), using default: 10", _maxConcurrentJobs);
            _maxConcurrentJobs = 10;
        }
        
        _queue = new JobQueue(_maxConcurrentJobs);
        _workerCount = Math.Max(2, _maxConcurrentJobs); // At least 2 workers, or match concurrency
        
        Log.Debug("JobScheduler initialized with {Workers} workers, {MaxConcurrent} max concurrent jobs, {Interval}s check interval", 
            _workerCount, _maxConcurrentJobs, _checkIntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Debug("Job Scheduler starting...");
        
        // Startup: Fix any corrupted NextRunTime values
        await FixCorruptedJobsOnStartup();
        
        // Wait a bit for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        
        Log.Information("Job Scheduler running. " +
                       "We'll be polling every {Interval} seconds, {Workers} workers, max {MaxConcurrent} concurrent", 
                       _checkIntervalSeconds, _workerCount, _maxConcurrentJobs);
        Log.Information("");
        
        // Start producer task (enqueues due jobs)
        var producerTask = Task.Run(async () => await ProducerLoopAsync(stoppingToken), stoppingToken);
        
        // Start consumer workers (execute jobs from queue)
        var consumerTasks = Enumerable.Range(0, _workerCount)
            .Select(workerId => Task.Run(async () => await ConsumerWorkerAsync(workerId, stoppingToken), stoppingToken))
            .ToArray();
        
        // Wait for cancellation
        try
        {
            await Task.WhenAll(new[] { producerTask }.Concat(consumerTasks));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Log.Debug("Job Scheduler cancellation requested");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error in Job Scheduler");
        }
        
        // Graceful shutdown: cancel all running jobs
        await CancelAllRunningJobsAsync();
        
        Log.Information("Job Scheduler stopped");
    }

    /// <summary>
    /// Producer loop: Poll database for due jobs and enqueue them
    /// </summary>
    private async Task ProducerLoopAsync(CancellationToken cancellationToken)
    {
        Log.Debug("Producer loop started");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueDueJobsAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in producer loop");
            }
            
            // Wait before next check
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_checkIntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        
        Log.Debug("Producer loop stopped");
    }

    /// <summary>
    /// Consumer worker: Dequeue and execute jobs
    /// </summary>
    private async Task ConsumerWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        Log.Debug("Consumer worker {WorkerId} started", workerId);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // This blocks until a job is available AND a slot is free
                var job = await _queue.DequeueAsync(cancellationToken);

                if (job == null)
                {
                    // Cancelled or error - backoff to prevent busy spinning when queue is empty
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }
                
                Log.Debug("Worker {WorkerId} picked up job {JobId} ({JobName})", 
                    workerId, job.Id, job.Name);
                
                try
                {
                    await ExecuteJobAsync(job, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Worker {WorkerId} error executing job {JobId}", workerId, job.Id);
                }
                finally
                {
                    // CRITICAL: Always release the slot
                    _queue.ReleaseSlot();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in consumer worker {WorkerId}", workerId);
                
                // Small delay to prevent tight error loops
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        
        Log.Debug("Consumer worker {WorkerId} stopped", workerId);
    }

    /// <summary>
    /// Enqueue all due jobs from database
    /// </summary>
    private async Task EnqueueDueJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
        
        // Get all due jobs from database
        var dueJobs = await jobService.GetDueJobsAsync();
        
        if (!dueJobs.Any())
        {
            return;
        }
        
        Log.Debug("Found {Count} due jobs to enqueue", dueJobs.Count());
        
        foreach (var job in dueJobs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            
            try
            {
                // Enqueue will check for duplicates
                var enqueued = await _queue.EnqueueAsync(job);
                
                if (enqueued)
                {
                    Log.Debug("Enqueued job {JobId} ({JobName}) - Priority: {Priority}", 
                        job.Id, job.Name, job.Priority);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error enqueuing job {JobId} ({JobName})", job.Id, job.Name);
            }
        }
    }

    /// <summary>
    /// Execute a single job
    /// </summary>
    private async Task ExecuteJobAsync(Job job, CancellationToken cancellationToken)
    {
        // Get or create execution lock for this job
        var executionLock = _executionLocks.GetOrAdd(job.Id, _ => new SemaphoreSlim(1, 1));
        
        // Try to acquire lock (non-blocking)
        if (!await executionLock.WaitAsync(0, cancellationToken))
        {
            Log.Debug("Job {JobId} is already being processed by another worker, skipping", job.Id);
            return;
        }
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
            var jobExecutor = scope.ServiceProvider.GetRequiredService<Reef.Api.JobExecutorService>();
            
            // Double-check if job is already running (database state might have changed)
            if (!job.AllowConcurrent && _runningJobs.ContainsKey(job.Id))
            {
                Log.Debug("Job {JobId} is already running and does not allow concurrent execution", job.Id);
                return;
            }

            // NOTE: Circuit breaker logic is handled in JobService.UpdateJobAfterFailure()
            // which checks ConsecutiveFailures against CircuitBreakerThreshold (10)
            // MaxRetries is for per-cycle retry attempts, not for disabling jobs

            // Create cancellation token for this job execution
            var jobCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // Add timeout
            if (job.TimeoutMinutes > 0)
            {
                jobCts.CancelAfter(TimeSpan.FromMinutes(job.TimeoutMinutes));
            }
            
            // Track running job
            _runningJobs.TryAdd(job.Id, jobCts);
            
            Log.Information("Executing job {JobId} ({JobName}) - Type: {JobType}, Priority: {Priority}", 
                job.Id, job.Name, job.Type, job.Priority);
            
            try
            {
                // Execute the job (this is the actual work)
                await jobExecutor.TriggerJobAsync(
                    job, 
                    ignoreDependencies: false, 
                    parameters: null);
                    
                Log.Information("Job {JobId} ({JobName}) execution completed", job.Id, job.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing job {JobId} ({JobName})", job.Id, job.Name);
            }
            finally
            {
                // Cleanup
                _runningJobs.TryRemove(job.Id, out _);
                jobCts?.Dispose();
            }
        }
        finally
        {
            executionLock.Release();
        }
    }

    /// <summary>
    /// Fix any corrupted jobs on startup (NextRunTime in the past)
    /// </summary>
    private async Task FixCorruptedJobsOnStartup()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var jobService = scope.ServiceProvider.GetRequiredService<JobService>();
            
            await jobService.FixCorruptedNextRunTimesAsync();
            
            Log.Debug("Startup cleanup completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup cleanup");
        }
    }

    /// <summary>
    /// Cancel all running jobs on shutdown
    /// </summary>
    private async Task CancelAllRunningJobsAsync()
    {
        Log.Information("Cancelling {Count} running jobs...", _runningJobs.Count);
        
        foreach (var (jobId, cts) in _runningJobs)
        {
            try
            {
                cts.Cancel();
                Log.Information("Cancelled job {JobId}", jobId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error cancelling job {JobId}", jobId);
            }
        }
        
        // Wait a bit for graceful shutdown
        await Task.Delay(TimeSpan.FromSeconds(2));
        
        // Cleanup
        _runningJobs.Clear();
        _executionLocks.Clear();
        await _queue.ClearAsync();
    }

    /// <summary>
    /// Get queue metrics for monitoring (called by API endpoints)
    /// </summary>
    public async Task<JobQueueMetrics> GetQueueMetricsAsync()
    {
        return await _queue.GetMetricsAsync();
    }

    public override void Dispose()
    {
        foreach (var cts in _runningJobs.Values)
        {
            try
            {
                cts?.Dispose();
            }
            catch { }
        }
        
        _runningJobs.Clear();
        _executionLocks.Clear();
        
        base.Dispose();
    }
}