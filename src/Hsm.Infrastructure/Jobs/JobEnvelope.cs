using System.Text.Json;
using Hsm.Application.Abstractions;

namespace Hsm.Infrastructure.Jobs;

/// <summary>
/// What travels on the wire for one job.
///
/// <list type="bullet">
/// <item><b>JobName</b> — the stable <see cref="JobNameAttribute"/> value
/// (<c>coms.send-email</c>), never an assembly-qualified CLR type name: type
/// names break on rename and deserializing one off a queue is a remote-code
/// footgun. <see cref="JobNameRegistry"/> maps the name back to the type.</item>
/// <item><b>Payload</b> — the command, serialized as JSON. Every queued command
/// carries identifiers only (see the module commands' doc comments); no secret
/// is ever placed on the queue.</item>
/// <item><b>Actor</b> — who enqueued it, captured at enqueue time so the
/// command authorizes in the worker exactly as it did at the edge. This is
/// identity, not credentials: an id, roles, and onboarding state — no token, no
/// password, nothing that grants access on its own.</item>
/// <item><b>Attempt</b> — 1 on the first delivery, incremented each time the
/// job is rescheduled for retry, so backoff and the attempt limit survive the
/// process that observed the failure.</item>
/// <item><b>UnknownAttempt</b> — how many times a worker has handed this job
/// back because it did not know <b>JobName</b> yet. A SEPARATE counter, because
/// what it measures is a rolling deploy rather than a failing job: spending the
/// queue's real attempts on it would dead-letter a perfectly good job within
/// seconds of a deploy starting (docs allows three attempts at a 2s base). It
/// defaults to zero, so envelopes written before this field existed read
/// correctly.</item>
/// </list>
/// </summary>
public sealed record JobEnvelope(
    string JobName, string Payload, RequestActor? Actor, int Attempt, int UnknownAttempt = 0)
{
    /// <summary>The single stream field the envelope JSON is stored under.</summary>
    public const string Field = "envelope";

    public string ToJson() => JsonSerializer.Serialize(this, JobJson.Options);

    public static JobEnvelope FromJson(string json) =>
        JsonSerializer.Deserialize<JobEnvelope>(json, JobJson.Options)
        ?? throw new InvalidOperationException("Job envelope deserialized to null.");
}

/// <summary>
/// A job waiting out its backoff (or its first-attempt delay) in the delayed
/// sorted set. The <see cref="Id"/> exists because a sorted set is a SET: two
/// byte-identical envelopes would collapse into one member and one of the two
/// jobs would be silently lost.
/// </summary>
internal sealed record DelayedJob(string Id, JobEnvelope Envelope)
{
    public string ToJson() => JsonSerializer.Serialize(this, JobJson.Options);

    public static DelayedJob FromJson(string json) =>
        JsonSerializer.Deserialize<DelayedJob>(json, JobJson.Options)
        ?? throw new InvalidOperationException("Delayed job deserialized to null.");
}

/// <summary>One cached options instance for every job (de)serialization (CA1869).</summary>
internal static class JobJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
