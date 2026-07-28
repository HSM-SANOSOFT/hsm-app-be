namespace Hsm.Contract.Tests;

/// <summary>
/// The shared mechanics of the capturing test doubles: a locked list with
/// snapshot reads, and an armable failure counter so the next N deliveries
/// throw instead of recording.
/// </summary>
public sealed class CapturingSink<T>(string failureMessage)
{
    private readonly List<T> _items = [];
    private int _failuresRemaining;

    public IReadOnlyList<T> Snapshot()
    {
        lock (_items)
        {
            return [.. _items];
        }
    }

    /// <summary>Arms the sink to fail the next <paramref name="count"/> records.</summary>
    public void FailNext(int count) => Interlocked.Exchange(ref _failuresRemaining, count);

    /// <summary>Throws when armed to fail; records otherwise.</summary>
    public void Record(T item)
    {
        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException(failureMessage);
        }

        lock (_items)
        {
            _items.Add(item);
        }
    }
}
