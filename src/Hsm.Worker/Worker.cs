namespace Hsm.Worker;

// Placeholder host loop; real background processing arrives with the
// communications module (plan U14).
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
