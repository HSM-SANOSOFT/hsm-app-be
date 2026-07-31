using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Coms.Commands.DispatchEmailBatch;
using Hsm.Application.Coms.Commands.ProcessWebhookEvent;
using Hsm.Application.Coms.Commands.ReceiveWebhook;
using Hsm.Application.Coms.Commands.ResendEmailBatch;
using Hsm.Application.Coms.Commands.ResendEmailRecipient;
using Hsm.Application.Coms.Commands.SendEmail;
using Hsm.Application.Coms.Queries.GetEmailBatch;
using Hsm.Application.Coms.Queries.GetEmailRecipient;
using Hsm.Application.Coms.Queries.ListEmailBatches;
using Hsm.Application.Coms.Queries.ListEmailRecipients;

namespace Hsm.Tests.Coms;

/// <summary>
/// Pins the Coms module's request policies directly on the request types.
///
/// The frozen <c>coms.controller.ts</c>/<c>coms-webhook.controller.ts</c>
/// (confirmed against <c>Hsm.Api.Coms.ComsEndpoints</c>'s own doc
/// comment and every <c>RequestAuth.GateAsync(ctx)</c> call in that file,
/// none of which carries a role argument) decorate all eight HTTP-facing
/// operations with a bare <c>@Roles()</c> — authenticated, onboarded, any
/// role — except the provider webhook, which is <c>@Public</c>: the HMAC
/// signature is the credential, so <see cref="ReceiveWebhookCommand"/> is
/// <see cref="AllowAnonymousRequestAttribute"/> with no role attribute.
///
/// The two job commands carry <see cref="JobNameAttribute"/> for Task 19's
/// job-name → type registry. Their policy attributes are intent, not
/// enforcement TODAY: <c>ComsJobProcessor.RunAsync</c> resolves and invokes
/// their handlers directly, bypassing <c>IDispatcher</c>/the pipeline
/// entirely (see that class's doc comment for why — briefly, the send-email
/// job persists a FAILED write and then re-throws so its retry loop sees the
/// exception, and <c>TransactionBehavior</c> would roll that persisted write
/// back on the re-throw). Task 19 must resolve that tension before either
/// policy attribute takes effect.
/// </summary>
public class ComsRequestPolicyTests
{
    public static TheoryData<Type> AuthenticatedRequests => new(
        typeof(SendEmailCommand),
        typeof(ListEmailBatchesQuery),
        typeof(GetEmailBatchQuery),
        typeof(ResendEmailBatchCommand),
        typeof(ListEmailRecipientsQuery),
        typeof(GetEmailRecipientQuery),
        typeof(ResendEmailRecipientCommand));

    [Theory]
    [MemberData(nameof(AuthenticatedRequests))]
    public void Every_http_facing_coms_request_requires_only_authentication(Type request)
    {
        Assert.NotNull(request);
        Assert.Null(request.GetCustomAttribute<RequireRoleAttribute>());
        Assert.Null(request.GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }

    [Fact]
    public void Receiving_a_provider_webhook_is_anonymous_the_signature_is_the_credential()
    {
        Assert.NotNull(typeof(ReceiveWebhookCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
        Assert.Null(typeof(ReceiveWebhookCommand).GetCustomAttribute<RequireRoleAttribute>());
    }

    [Fact]
    public void Dispatching_an_email_batch_job_carries_its_frozen_queue_name()
    {
        var attribute = typeof(DispatchEmailBatchCommand).GetCustomAttribute<JobNameAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("coms.send-email", attribute!.Name);
    }

    [Fact]
    public void Processing_a_webhook_event_job_carries_its_frozen_queue_name_and_is_anonymous()
    {
        var attribute = typeof(ProcessWebhookEventCommand).GetCustomAttribute<JobNameAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("coms.process-webhook", attribute!.Name);
        Assert.NotNull(typeof(ProcessWebhookEventCommand).GetCustomAttribute<AllowAnonymousRequestAttribute>());
    }
}
