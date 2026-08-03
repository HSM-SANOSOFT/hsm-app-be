using Hsm.Infrastructure.Jobs;
using StackExchange.Redis;

namespace Hsm.Worker;

/// <summary>
/// A Redis lease: the right to be the one process doing something, held for
/// <c>ttl</c> at a time and renewed while the holder is alive.
///
/// <para><b>Why it exists.</b> A queue whose ordering guarantee is "no newer
/// job starts while an earlier one is still working through its attempts"
/// cannot be enforced by a consumer counting its own loops — two worker
/// replicas each count one loop and take a job each. Only a shared claim
/// makes "one consumer" true across processes, and Redis (which is already the
/// queue) is the obvious place to keep it.</para>
///
/// <para><b>Mechanics.</b> <c>SET key holder NX PX ttl</c> to take it: exactly
/// one caller gets it because <c>NX</c> is atomic. Renewal and release are
/// compare-and-swap scripts against <c>holder</c>, never a bare
/// <c>PEXPIRE</c>/<c>DEL</c> — a process that stalled long enough to lose its
/// lease must not extend or delete the lease its successor now holds.</para>
///
/// <para><b>What it does not give you, and the invariant that makes up for
/// it.</b> This is a lease, not a fence: nothing stops a holder that has
/// already lost the lease from carrying on, because it only finds out at its
/// next check. The dangerous version of that is not an exotic pause — it is
/// ORDINARY WORK. Renewal happens between units of work, so any stretch of work
/// done without a check must fit inside the TTL, and a batch of ten slow sends
/// at a couple of seconds each does not fit inside fifteen. Hence the
/// invariant, in two halves, both enforced rather than hoped for:</para>
///
/// <list type="number">
/// <item>a serial queue drains ONE job per lease check
/// (<c>JobConsumerService.BatchSizeFor</c>), so the unchecked stretch is one
/// job; and</item>
/// <item><c>LeaseTtl &gt; ClaimMinIdle</c> for every serial queue — the lease
/// outlives the longest job the deployment says that queue may have —
/// refused at startup by <c>JobConsumerService.StartAsync</c> if it does
/// not hold.</item>
/// </list>
///
/// <para>What is left over is genuinely exceptional: a process frozen for
/// longer than a whole job's worst case. The queue survives even that —
/// delivery is at-least-once and handlers are idempotent — but the ORDERING
/// guarantee is degraded while it lasts, and that is the residue this design
/// accepts.</para>
/// </summary>
public sealed class QueueLease(IJobConnection connection, string key, string holder, TimeSpan ttl)
{
    /// <summary>Extend only what is still ours. KEYS[1] lease, ARGV[1] holder, ARGV[2] ttl ms.</summary>
    private const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";

    /// <summary>Release only what is still ours. KEYS[1] lease, ARGV[1] holder.</summary>
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private DateTimeOffset _renewAt = DateTimeOffset.MinValue;
    private bool _held;

    /// <summary>How long the lease survives a holder that stops renewing it.</summary>
    public TimeSpan Ttl => ttl;

    /// <summary>
    /// True while this instance holds the lease: renews it when it is due,
    /// otherwise tries to take it. Cheap to call in a tight loop — between
    /// renewals it answers from local state without a round trip.
    /// </summary>
    public async Task<bool> TryHoldAsync()
    {
        var db = await connection.GetDatabaseAsync().ConfigureAwait(false);

        if (_held)
        {
            if (DateTimeOffset.UtcNow < _renewAt)
            {
                return true;
            }

            var renewed = await db.ScriptEvaluateAsync(
                RenewScript,
                [key],
                [holder, (long)ttl.TotalMilliseconds]).ConfigureAwait(false);
            if ((long)renewed != 0)
            {
                _renewAt = NextRenewal();
                return true;
            }

            // Renewal failed, so the lease is not ours any more — it expired
            // while this process was not looking. Fall through and compete for
            // it again rather than carry on consuming without it.
            _held = false;
        }

        _held = await db.StringSetAsync(key, holder, ttl, When.NotExists).ConfigureAwait(false);
        if (_held)
        {
            _renewAt = NextRenewal();
        }

        return _held;
    }

    /// <summary>
    /// Hands the lease back so a sibling can take it on the next poll instead
    /// of waiting out the TTL. Best-effort: a shutdown that cannot reach Redis
    /// still ends, and the TTL releases it.
    /// </summary>
    public async Task ReleaseAsync()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        try
        {
            var db = await connection.GetDatabaseAsync().ConfigureAwait(false);
            await db.ScriptEvaluateAsync(ReleaseScript, [key], [holder]).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutdown must not fail on a lease that expires by itself.
        }
    }

    /// <summary>A third of the TTL: two renewals may be lost before it lapses.</summary>
    private DateTimeOffset NextRenewal() => DateTimeOffset.UtcNow + (ttl / 3);
}
