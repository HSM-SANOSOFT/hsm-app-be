namespace Hsm.Application.Abstractions;

/// <summary>
/// This command owns its own commit boundaries: <c>TransactionBehavior</c> does
/// not open one around it.
///
/// <para><b>Why it exists.</b> "Commit only if the handler returns normally" is
/// the right rule for a request. It is the wrong rule for a job whose contract
/// is <i>record what happened and then fail</i>. The send-email and
/// document-render handlers persist a FAILED status, a per-recipient error
/// message and a template parse-log row and THEN re-throw, so the queue counts
/// the attempt and backs off — under a pipeline-owned transaction the re-throw
/// rolls back every one of those writes, and the job's own retry accounting
/// reads state that never existed. That is not a status row this system could
/// special-case: it is a general property of a handler whose failure path is
/// observable. The boundary is what has to move, not the individual
/// writes.</para>
///
/// <para>The rest of the pipeline still applies — telemetry, authorization
/// against the enqueuing actor, validation. Only the transaction is the
/// handler's own business, exactly as it was before these commands were routed
/// through a dispatcher at all: each store call commits independently and each
/// is atomic on its own.</para>
///
/// <para>A second, quieter reason to keep it off these two: a render job holds
/// its "transaction" across a PDF render and an S3 upload. A database
/// transaction open across seconds of external I/O is a connection-pool and
/// lock-duration problem waiting for load to find it.</para>
///
/// <para><b>This is not a general escape hatch.</b> Reach for it only when the
/// handler has a failure path that must persist. The two commands carrying it
/// are pinned by <c>ComsRequestPolicyTests</c> and <c>DocsRequestPolicyTests</c>
/// so the choice stays visible.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoAmbientTransactionAttribute : Attribute;
