namespace Hsm.Worker;

// Heartbeat loop. The communications background dispatch (plan U14) is the
// ComsJobProcessor hosted service registered by AddHsmInfrastructure — the
// queue-hop topology decision is documented on ChannelComsDispatcher.
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
