using StackExchange.Redis;

namespace Hsm.Infrastructure.Jobs;

/// <summary>The queue's Redis handle, opened on first use.</summary>
public interface IJobConnection
{
    Task<IDatabase> GetDatabaseAsync();
}

/// <summary>
/// A <see cref="ConnectionMultiplexer"/> created on the FIRST queue operation,
/// never during host boot.
///
/// <para>That is a hard requirement, not an optimization. Every host registers
/// the job queue, but a host that neither enqueues nor consumes must not open a
/// socket to Redis just by starting — <c>Hsm.Web</c> in production, and every
/// <c>WebApplicationFactory</c>-booted host in <c>tests/Hsm.Tests</c>, which is
/// the no-containers suite and would fail at boot against a Redis that isn't
/// there. Connecting lazily makes "registered" cost nothing until "used".</para>
///
/// <para>A failed connect leaves the field null rather than caching the failed
/// task, so the next call retries instead of failing forever on a Redis that
/// was merely slow to start.</para>
/// </summary>
public sealed class RedisJobConnection(string configuration) : IJobConnection, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _multiplexer;

    public async Task<IDatabase> GetDatabaseAsync()
    {
        var multiplexer = _multiplexer;
        if (multiplexer is not null)
        {
            return multiplexer.GetDatabase();
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _multiplexer ??= await ConnectionMultiplexer.ConnectAsync(configuration).ConfigureAwait(false);
            return _multiplexer.GetDatabase();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
