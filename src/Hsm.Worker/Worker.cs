namespace Hsm.Worker;

// Heartbeat loop. Background job processing (plan U14/Task 19) belongs here
// but does not live here yet: Task 20 registers the queue's consume loop in
// this host. Until then Hsm.Api consumes its own enqueues — see
// JobQueueRegistration.AddInHostJobConsumer.
public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker host started")]
    private static partial void LogStarted(ILogger logger);
}
