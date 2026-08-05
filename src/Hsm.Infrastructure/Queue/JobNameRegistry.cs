using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Hsm.Application.Abstractions;

namespace Hsm.Infrastructure.Queue;

/// <summary>
/// The name ↔ type map for queued commands, built once at startup by scanning
/// for <see cref="JobNameAttribute"/>.
///
/// <para>Job payloads never carry CLR type names. An assembly-qualified name on
/// a queue breaks the moment a type is renamed or moved — with jobs already in
/// flight, which is exactly when nothing may break — and turns the consumer
/// into a "deserialize whatever the payload names" gadget. A short, stable,
/// deliberately chosen name (<c>coms.send-email</c>) that only resolves to a
/// type this registry already knows has neither problem.</para>
///
/// <para>The name's first segment is its QUEUE (<c>coms</c>, <c>docs</c>), so a
/// job's queue is a property of the job rather than a second thing every call
/// site has to remember to pass.</para>
/// </summary>
public sealed class JobNameRegistry
{
    private readonly Dictionary<string, Type> _byName;
    private readonly Dictionary<Type, string> _byType;

    private JobNameRegistry(Dictionary<string, Type> byName, Dictionary<Type, string> byType)
    {
        _byName = byName;
        _byType = byType;
    }

    /// <summary>Every job name known to this registry.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>Scans <paramref name="assemblies"/> for <see cref="JobNameAttribute"/> types.</summary>
    public static JobNameRegistry Scan(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, string>();
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            var attribute = type.GetCustomAttribute<JobNameAttribute>();
            if (attribute is null)
            {
                continue;
            }

            if (byName.TryGetValue(attribute.Name, out var existing) && existing != type)
            {
                throw new InvalidOperationException(
                    $"Duplicate job name '{attribute.Name}' on {existing.Name} and {type.Name}.");
            }

            byName[attribute.Name] = type;
            byType[type] = attribute.Name;
        }

        return new JobNameRegistry(byName, byType);
    }

    public string NameOf(Type commandType) =>
        _byType.TryGetValue(commandType, out var name)
            ? name
            : throw new InvalidOperationException(
                $"{commandType?.Name} carries no [JobName]; it cannot be queued.");

    /// <summary>
    /// The type for <paramref name="jobName"/>, or false if this process has
    /// never heard of it.
    ///
    /// <para>Deliberately a Try: during a rolling deploy an old worker will be
    /// handed jobs a newer producer introduced, and "I do not know this name"
    /// has to be distinguishable from "this envelope is garbage". The first is
    /// temporary and worth retrying; only the second is hopeless. See
    /// <see cref="RedisStreamJobConsumer"/>.</para>
    /// </summary>
    public bool TryTypeOf(string jobName, [NotNullWhen(true)] out Type? commandType) =>
        _byName.TryGetValue(jobName, out commandType);

    /// <summary>The queue a job name belongs to — its first dotted segment.</summary>
    public static string QueueOf(string jobName)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobName);
        var separator = jobName.IndexOf('.', StringComparison.Ordinal);
        return separator > 0
            ? jobName[..separator]
            : throw new InvalidOperationException(
                $"Job name '{jobName}' has no queue segment (expected '<queue>.<job>').");
    }
}
