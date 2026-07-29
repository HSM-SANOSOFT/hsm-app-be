---
title: "Clean Architecture + CQRS, three deployable hosts, durable worker"
type: refactor
status: active
date: 2026-07-28
origin: design conversation 2026-07-28 (no separate brainstorm document)
supersedes_decisions: "U14 in-process channel queue; single-host topology"
---

# Clean + CQRS Restructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development`
> (recommended) or `superpowers:executing-plans` to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking. `ce-work` can also execute it —
> the `## Tasks` sections below are its implementation units.

**Goal:** Reshape `Hsm.Application` into command/query slices behind a dispatcher with
authorization/validation/transaction/telemetry behaviors, split the single host into three
independently deployable processes, replace the job-losing in-memory queue with Redis Streams,
and make the repository deployable at all.

**Architecture:** Clean Architecture with dependencies pointing inward only. One application
core (`Hsm.Domain` ← `Hsm.Application` ← `Hsm.Infrastructure`) with three doors onto it:
`Hsm.Api` (REST/FHIR for external integrations), `Hsm.Web` (Blazor Server for staff, calling
handlers **in-process** — no internal HTTP API for our own UI), and `Hsm.Worker` (durable
queue consumer + scheduled work). CQRS here means command/query segregation with a pipeline —
one database, no event sourcing.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core + Npgsql, Blazor Server + MudBlazor 9.7,
StackExchange.Redis (Streams), xUnit + bUnit, OpenTelemetry, QuestPDF, Firely SDK.

## Global Constraints

- **No MediatR.** v13+ is commercially licensed; `docs/ARCHITECTURE-DECISIONS.md` §1 forbids
  restrictive licenses. The dispatcher is hand-rolled.
- **Warnings are errors.** `Directory.Build.props` sets `TreatWarningsAsErrors`. Analyzer
  violations fail the build — fix, never blanket-suppress.
- **Central package management.** Versions only in `Directory.Packages.props`; project files
  carry `<PackageReference Include="..." />` with no `Version`.
- **`dotnet format Hsm.sln --verify-no-changes` must exit 0** before every commit.
- **The frozen HTTP contract does not move.** `tests/Hsm.Contract.Tests` (258 tests) exercise
  HTTP and never name a handler class. They are the safety net for every task here and must
  pass **unmodified**, except where a task explicitly retargets the host factory. A failing
  contract test means the refactor changed behavior — fix the code, never the test.
- **All dotnet commands run in the dev container:**
  `devcontainer exec --workspace-folder /home/rs/Documents/hsm/hsm-app bash -c 'cd /workspace && <cmd>'`
- **Seven source projects.** No new source project is created by this plan.
- Infrastructure services inside the container: `postgres:5432`, `redis:6379`, `rustfs:9000`,
  `meilisearch:7700`, `otel-collector:4317/4318`.

---

## Requirements

**Application shape**
- R1. Commands and queries are distinct types dispatched through one pipeline.
- R2. Authorization is a property of the request, enforced once, regardless of transport.
- R3. Validation, transaction scope, and per-use-case telemetry are pipeline behaviors.
- R4. No commercially licensed mediator dependency.
- R5. One statable rule for where ports live.

**Topology**
- R6. Three deployables, independently scalable and deployable.
- R7. The Blazor UI calls handlers in-process. No internal HTTP API for our own UI.
- R8. `Hsm.Api` serves external integration consumers only.
- R9. Background work runs in `Hsm.Worker`, not in a request-serving host.

**UI boundary (option B)**
- R10. Screens live as folders inside `Hsm.Web`; `Hsm.Web.Components` is deleted.
- R11. The client-isolation boundary is preserved by an architecture test, not the compiler.

**Durability**
- R12. Queued work survives restart and coordinates across instances.
- R13. Scheduled/cron work has a home, firing once across N workers.

**Deployability**
- R14. EF migrations exist with a stated owner for applying them.
- R15. Each host has a Dockerfile and an `hsm-app-*` compose service.
- R16. Telemetry destination is chosen by application configuration.

**Ambiguity elimination**
- R17. A conventions document states where each kind of code goes; `Users` is the reference slice.

---

## Scope Boundaries

- No event sourcing, no CQRS read models, no separate read/write databases.
- No behavior changes to the frozen contract; no new `/v1` or `/fhir` routes.
- No WASM/mobile host. Option B knowingly defers screen extraction until mobile is scheduled.
- No sticky-session / Redis SignalR backplane work for multiple `Hsm.Web` instances.
- No Kubernetes.
- No new source project (explicitly: the `Hsm.Hosting` project floated in an earlier draft is
  **retracted** — the genuine cross-host overlap is ~130 lines, handled by Task 17).

---

## Key Technical Decisions

- **Hand-rolled dispatcher with cached reflection.** `IDispatcher.Send<TResult>(IRequest<TResult>)`
  resolves `IRequestHandler<TRequest,TResult>` once per request type into a compiled delegate
  held in a `ConcurrentDictionary`. Call sites stay clean; no framework.
- **Behavior order: Telemetry → Authorization → Validation → Transaction → Handler.** Telemetry
  outermost so it records rejections; transaction innermost so it never wraps a request that was
  going to be refused.
- **Queries never open a transaction.** `TransactionBehavior` is registered for `ICommand` only.
- **Queued commands carry their actor.** The queue envelope holds the enqueuing principal, so
  `AuthorizationBehavior` and audit trails work identically for HTTP and worker dispatch.
- **Redis Streams, not lists.** Consumer groups give exactly-one-consumer-per-group delivery with
  explicit `XACK`, and `XAUTOCLAIM` recovers messages from dead consumers — neither is expressible
  with `BRPOPLPUSH`. Retry backoff uses a sorted set scored by due-timestamp, drained into the
  stream by a poller, because Streams cannot delay delivery natively.
- **Stable job names, not assembly-qualified type names.** `[JobName("coms.send-email")]` maps to
  a `Type` through a startup registry. Assembly-qualified names in a payload break on any rename
  and are a deserialization footgun.
- **Coms stays strictly serial** (consumer group with one consumer). The Phase 4 consolidation pass
  established that frozen resend ordering depends on it. Docs runs 4-wide.
- **Both `Hsm.Api` and `Hsm.Web` reference `Hsm.Application` directly.** Two doors, one core.
- **Cookie policy is contract; cookie plumbing is per-host.** Cookie names/paths/SameSite/max-age
  move to `Hsm.Contracts`; each host keeps its own ~30-line extension method. U12's contract tests
  already pin the exact attributes, so drift is caught.
- **Migrations are applied by an explicit step, never on host boot.** Two request-serving hosts
  racing `Migrate()` is a known failure mode.
- **The scheduler leases via `SET NX PX`.** One worker fires a given schedule; no new dependency.

---

## File Structure

Target layout. `Hsm.Web.Components` disappears; no project is added.

```
src/
  Hsm.Domain/                       unchanged — entities, invariants
  Hsm.Application/
    Abstractions/
      IRequest.cs                   IRequest/ICommand/IQuery/Unit
      IRequestHandler.cs            handler + IPipelineBehavior + delegate
      IDispatcher.cs                dispatcher contract
      Dispatcher.cs                 cached-reflection implementation
      RequireRoleAttribute.cs       + AllowAnonymousRequestAttribute
      ICurrentPrincipal.cs          port: who is acting (host-supplied)
      IValidator.cs                 port: per-request validation
      JobNameAttribute.cs           stable queue name for a command
      Behaviors/
        TelemetryBehavior.cs  AuthorizationBehavior.cs
        ValidationBehavior.cs  TransactionBehavior.cs
    Users/
      Commands/UpdateOwnProfile/{Command,Handler,Validator}.cs
      Commands/ChangeOwnPassword/…  Commands/CreateStaffUser/…  Commands/ChangeUserRole/…
      Queries/ListUsers/{Query,Handler}.cs   Queries/GetUser/…
      IUserStore.cs                 module port beside its module
    Auth/ Settings/ Templates/ Coms/ Docs/ Clinical/   same slice shape
    Ports/
      IObjectStorage.cs  ISearchIndex.cs  IJobQueue.cs   ONLY infra-wide ports
  Hsm.Infrastructure/
    Jobs/RedisStreamJobQueue.cs     replaces Jobs/ChannelJobQueue.cs
    Jobs/JobNameRegistry.cs  Jobs/DelayedJobPump.cs
    Migrations/                     NEW — first migrations in the repo
  Hsm.Contracts/
    Auth/AuthCookiePolicy.cs        NEW — shared cookie values
  Hsm.Api/                          NEW host — endpoints moved out of Hsm.Web
    Program.cs  Api/{ApiEnvelope,Validation,ApiErrorHandling,ErrorStatusCodes}.cs
    Auth/{AuthEndpoints,CsrfProtection,RequestAuth,AuthCookies}.cs
    {Users,Settings,Templates,Coms,Docs,Fhir,Health}/*Endpoints.cs
  Hsm.Web/                          Blazor host + the screens (option B)
    Program.cs  Host/App.razor
    Pages/  Layout/  HsmTheme.cs  Routes.razor  _Imports.razor    ← from the deleted RCL
    Auth/{HsmCookieAuthenticationHandler,HsmAuthenticationStateProvider,
          AuthPrincipalClaims,RequestAuth,AuthCookies}.cs
    Services/*UiService.cs
  Hsm.Worker/
    Program.cs  JobConsumerService.cs  Scheduling/{Scheduler,ScheduleRegistry}.cs
tests/
  Hsm.Tests/                        NEW — architecture + pipeline unit tests, no infra
  Hsm.Contract.Tests/               retargeted at Hsm.Api
  Hsm.Integration.Tests/            + Redis queue durability tests
  Hsm.Web.Tests/                    renamed from Hsm.Web.Components.Tests
```

Deleted: `src/Hsm.Web.Components/`, `src/Hsm.Infrastructure/Jobs/ChannelJobQueue.cs`,
`tests/Hsm.Domain.Tests/`, `tests/Hsm.Application.Tests/`, all `*/Ports.cs` aggregate files.

---

## Tasks

### Task 1: Unify the ports split

**Files:**
- Delete: `src/Hsm.Application/{Auth,Users,Settings,Templates,Coms,Docs,Clinical}/Ports.cs`
- Create: one file per interface beside its slice, e.g. `src/Hsm.Application/Users/IUserStore.cs`
- Keep: `src/Hsm.Application/Ports/IObjectStorage.cs`, `Ports/ISearchIndex.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: unchanged public interfaces at new paths. Namespaces are unchanged
  (`Hsm.Application.Users` etc.), so no consumer edits are required.

- [ ] **Step 1: Split each aggregate Ports.cs**

For each module, move every interface into its own file named after it, in the same folder and
namespace. Example — `src/Hsm.Application/Users/Ports.cs` becomes `IUserStore.cs`,
`IStaffWelcomeEmailer.cs`, etc.

- [ ] **Step 2: Verify no namespace changed**

Run: `git diff --stat` — expect only renames/moves, and
`grep -rn "namespace Hsm.Application" src/Hsm.Application | sort -u` unchanged from before.

- [ ] **Step 3: Build and test**

Run: `dotnet build Hsm.sln && dotnet test Hsm.sln`
Expected: 0 warnings, 305 tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "refactor: one file per port, beside its module

Module ports live with their module; Ports/ holds only infrastructure-wide
ports (blob, search). Ends the two-locations inconsistency."
```

---

### Task 2: CQRS abstractions and the dispatcher

**Files:**
- Create: `src/Hsm.Application/Abstractions/IRequest.cs`, `IRequestHandler.cs`,
  `IDispatcher.cs`, `Dispatcher.cs`
- Test: `tests/Hsm.Tests/Abstractions/DispatcherTests.cs` (create the project in this task)

**Interfaces:**
- Consumes: nothing.
- Produces — every later task depends on these exact signatures:
  - `IRequest<out TResult>` — marker
  - `ICommand<out TResult> : IRequest<TResult>`, `ICommand : ICommand<Unit>`
  - `IQuery<out TResult> : IRequest<TResult>`
  - `readonly record struct Unit { public static Unit Value { get; } }`
  - `IRequestHandler<in TRequest, TResult>.HandleAsync(TRequest, CancellationToken) : Task<TResult>`
  - `IPipelineBehavior<TRequest, TResult>.HandleAsync(TRequest, RequestHandlerDelegate<TResult>, CancellationToken) : Task<TResult>`
  - `delegate Task<TResult> RequestHandlerDelegate<TResult>()`
  - `IDispatcher.Send<TResult>(IRequest<TResult>, CancellationToken = default) : Task<TResult>`

- [ ] **Step 1: Create the `Hsm.Tests` project**

```bash
dotnet new xunit -o tests/Hsm.Tests -n Hsm.Tests
dotnet sln Hsm.sln add tests/Hsm.Tests/Hsm.Tests.csproj
dotnet add tests/Hsm.Tests reference src/Hsm.Application src/Hsm.Domain src/Hsm.Contracts
rm tests/Hsm.Tests/UnitTest1.cs
```

Strip `TargetFramework`/`Nullable`/`ImplicitUsings` and all `Version=` attributes from the new
csproj — `Directory.Build.props` and `Directory.Packages.props` own them.

- [ ] **Step 2: Write the failing dispatcher test**

```csharp
using Hsm.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Tests.Abstractions;

public class DispatcherTests
{
    private sealed record Ping(string Text) : IQuery<string>;

    private sealed class PingHandler : IRequestHandler<Ping, string>
    {
        public Task<string> HandleAsync(Ping request, CancellationToken ct)
            => Task.FromResult($"pong:{request.Text}");
    }

    [Fact]
    public async Task Dispatches_to_the_handler_registered_for_the_request_type()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<Ping, string>, PingHandler>();
        services.AddSingleton<IDispatcher, Dispatcher>();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDispatcher>()
            .Send(new Ping("hi"), TestContext.Current.CancellationToken);

        Assert.Equal("pong:hi", result);
    }

    [Fact]
    public async Task Missing_handler_throws_a_named_error()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher, Dispatcher>();
        using var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IDispatcher>()
                .Send(new Ping("hi"), TestContext.Current.CancellationToken));

        Assert.Contains(nameof(Ping), ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run it and confirm it fails**

Run: `dotnet test tests/Hsm.Tests`
Expected: FAIL — `IRequest`, `IDispatcher`, `Dispatcher` do not exist (compile error).

- [ ] **Step 4: Write `IRequest.cs`**

```csharp
namespace Hsm.Application.Abstractions;

/// <summary>A dispatchable request. Use ICommand or IQuery, never this directly.</summary>
public interface IRequest<out TResult>;

/// <summary>Mutates state. Runs inside a transaction.</summary>
public interface ICommand<out TResult> : IRequest<TResult>;

/// <summary>A command with no meaningful result.</summary>
public interface ICommand : ICommand<Unit>;

/// <summary>Reads state. Never opens a transaction.</summary>
public interface IQuery<out TResult> : IRequest<TResult>;

/// <summary>The absence of a result — commands that return nothing.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
```

- [ ] **Step 5: Write `IRequestHandler.cs`**

```csharp
namespace Hsm.Application.Abstractions;

public interface IRequestHandler<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken ct);
}

/// <summary>Invokes the next stage of the pipeline (or the handler itself).</summary>
public delegate Task<TResult> RequestHandlerDelegate<TResult>();

/// <summary>
/// A cross-cutting stage. Registered order is outermost-first; see the plan's
/// behavior-order decision (telemetry, authorization, validation, transaction).
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct);
}
```

- [ ] **Step 6: Write `IDispatcher.cs` and `Dispatcher.cs`**

```csharp
namespace Hsm.Application.Abstractions;

public interface IDispatcher
{
    Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default);
}
```

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Application.Abstractions;

/// <summary>
/// Resolves the handler for a request's concrete type and wraps it in the
/// registered behavior chain. The open generic (IRequest&lt;TResult&gt;) means the
/// concrete type is only known at runtime, so the invoker is built once per
/// request type by reflection and cached — the reflection cost is paid on first
/// dispatch, never per call.
/// </summary>
public sealed class Dispatcher(IServiceProvider provider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<TResult> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invoker = (Func<IServiceProvider, IRequest<TResult>, CancellationToken, Task<TResult>>)
            Invokers.GetOrAdd(request.GetType(), static type => BuildInvoker<TResult>(type));

        return invoker(provider, request, ct);
    }

    private static object BuildInvoker<TResult>(Type requestType)
    {
        var method = typeof(Dispatcher)
            .GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(requestType, typeof(TResult));

        return method.CreateDelegate(
            typeof(Func<IServiceProvider, IRequest<TResult>, CancellationToken, Task<TResult>>));
    }

    private static Task<TResult> InvokeTyped<TRequest, TResult>(
        IServiceProvider provider, IRequest<TResult> request, CancellationToken ct)
        where TRequest : IRequest<TResult>
    {
        var handler = provider.GetService<IRequestHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler registered for {typeof(TRequest).Name}.");

        var typed = (TRequest)request;
        RequestHandlerDelegate<TResult> next = () => handler.HandleAsync(typed, ct);

        // Behaviors apply outermost-first, so wrap in reverse registration order.
        var behaviors = provider.GetServices<IPipelineBehavior<TRequest, TResult>>().ToArray();
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = next;
            next = () => behavior.HandleAsync(typed, inner, ct);
        }

        return next();
    }
}
```

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet test tests/Hsm.Tests`
Expected: PASS, 2 tests.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: CQRS abstractions and a hand-rolled dispatcher

IRequest/ICommand/IQuery, IRequestHandler, IPipelineBehavior, and a
dispatcher that caches a per-request-type invoker. No MediatR — v13+ is
commercially licensed and the product may ship to other hospitals."
```

---

### Task 3: `ICurrentPrincipal` port and host implementations

**Files:**
- Create: `src/Hsm.Application/Abstractions/ICurrentPrincipal.cs`
- Test: `tests/Hsm.Tests/Abstractions/CurrentPrincipalTests.cs`

**Interfaces:**
- Consumes: Task 2's abstractions.
- Produces:
  - `interface ICurrentPrincipal { RequestActor? Actor { get; } }`
  - `sealed record RequestActor(string Id, IReadOnlyList<string> Roles, bool OnboardingCompleted)`
  - `sealed class AmbientPrincipal : ICurrentPrincipal` with `void Set(RequestActor?)` — used by
    the worker to install the enqueuing actor for the duration of one job.

- [ ] **Step 1: Write the failing test**

```csharp
using Hsm.Application.Abstractions;

namespace Hsm.Tests.Abstractions;

public class CurrentPrincipalTests
{
    [Fact]
    public void Ambient_principal_returns_what_was_set()
    {
        var ambient = new AmbientPrincipal();
        Assert.Null(ambient.Actor);

        ambient.Set(new RequestActor("u1", ["admin"], OnboardingCompleted: true));

        Assert.Equal("u1", ambient.Actor!.Id);
        Assert.Contains("admin", ambient.Actor.Roles);
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/Hsm.Tests --filter CurrentPrincipalTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write `ICurrentPrincipal.cs`**

```csharp
namespace Hsm.Application.Abstractions;

/// <summary>Who is acting. Supplied by the host: HTTP context, Blazor circuit, or queue envelope.</summary>
public interface ICurrentPrincipal
{
    RequestActor? Actor { get; }
}

/// <summary>The acting identity as the pipeline needs it.</summary>
public sealed record RequestActor(string Id, IReadOnlyList<string> Roles, bool OnboardingCompleted)
{
    public bool IsInRole(string role) => Roles.Contains(role);
}

/// <summary>
/// Scoped, settable principal. The worker installs the actor captured when the
/// job was enqueued, so a queued command authorizes exactly as its HTTP
/// counterpart did.
/// </summary>
public sealed class AmbientPrincipal : ICurrentPrincipal
{
    public RequestActor? Actor { get; private set; }
    public void Set(RequestActor? actor) => Actor = actor;
}
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `dotnet test tests/Hsm.Tests --filter CurrentPrincipalTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: ICurrentPrincipal port for pipeline authorization

The acting identity is host-supplied: HTTP context, Blazor circuit, or the
principal captured in a queued job's envelope."
```

---

### Task 4: `TelemetryBehavior` — prove the chain works

**Files:**
- Create: `src/Hsm.Application/Abstractions/Behaviors/TelemetryBehavior.cs`
- Create: `src/Hsm.Application/Abstractions/PipelineRegistration.cs`
- Test: `tests/Hsm.Tests/Abstractions/TelemetryBehaviorTests.cs`

**Interfaces:**
- Consumes: `IPipelineBehavior<,>`, `RequestHandlerDelegate<>` (Task 2).
- Produces:
  - `static class RequestActivity { public const string SourceName = "Hsm.Application"; }`
  - `static IServiceCollection AddHsmPipeline(this IServiceCollection services)` — registers all
    four behaviors in outermost-first order. Later tasks add to this one method, so hosts never
    enumerate behaviors themselves.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics;
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class TelemetryBehaviorTests : IDisposable
{
    private sealed record Ping : IQuery<string>;

    private readonly ActivityListener _listener;
    private readonly List<Activity> _started = [];

    public TelemetryBehaviorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RequestActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _started.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task Span_is_named_after_the_request_type()
    {
        var behavior = new TelemetryBehavior<Ping, string>();

        var result = await behavior.HandleAsync(
            new Ping(), () => Task.FromResult("ok"), TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Contains(_started, a => a.DisplayName == nameof(Ping));
    }

    [Fact]
    public async Task Failing_request_marks_the_span_as_error()
    {
        var behavior = new TelemetryBehavior<Ping, string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.HandleAsync(
            new Ping(),
            () => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken));

        var span = Assert.Single(_started, a => a.DisplayName == nameof(Ping));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/Hsm.Tests --filter TelemetryBehaviorTests`
Expected: FAIL — `TelemetryBehavior` and `RequestActivity` do not exist.

- [ ] **Step 3: Write `TelemetryBehavior.cs`**

```csharp
using System.Diagnostics;

namespace Hsm.Application.Abstractions.Behaviors;

public static class RequestActivity
{
    public const string SourceName = "Hsm.Application";
    internal static readonly ActivitySource Source = new(SourceName);
}

/// <summary>
/// Outermost behavior: every dispatched request gets a span named after its
/// type, including requests the pipeline goes on to reject. OpenTelemetry is
/// already wired in every host (plan 2026-07-27-001 U10); this only needs the
/// meter/source name added to the hosts' AddMeter/AddSource lists.
/// </summary>
public sealed class TelemetryBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        using var activity = RequestActivity.Source.StartActivity(typeof(TRequest).Name);
        try
        {
            var result = await next().ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
```

- [ ] **Step 4: Write `PipelineRegistration.cs`**

```csharp
using Hsm.Application.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Application.Abstractions;

public static class PipelineRegistration
{
    /// <summary>
    /// Registers the dispatcher and the behavior chain, outermost first.
    /// Order is load-bearing: telemetry records rejections, authorization
    /// refuses before validation spends work, and the transaction is innermost
    /// so it never wraps a request that was going to be refused.
    /// </summary>
    public static IServiceCollection AddHsmPipeline(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
        return services;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test tests/Hsm.Tests --filter TelemetryBehaviorTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: telemetry behavior and pipeline registration

Every dispatched request gets a span named after its type, rejections
included. AddHsmPipeline is the single registration point hosts call."
```

---

### Task 5: `AuthorizationBehavior` — the defect this plan exists to fix

**Files:**
- Create: `src/Hsm.Application/Abstractions/RequireRoleAttribute.cs`,
  `Abstractions/Behaviors/AuthorizationBehavior.cs`
- Modify: `src/Hsm.Application/Abstractions/PipelineRegistration.cs`
- Test: `tests/Hsm.Tests/Abstractions/AuthorizationBehaviorTests.cs`

**Interfaces:**
- Consumes: `ICurrentPrincipal`, `RequestActor` (Task 3); `IPipelineBehavior<,>` (Task 2).
- Produces:
  - `[AttributeUsage(AttributeTargets.Class)] sealed class RequireRoleAttribute(params string[] roles)`
    with `IReadOnlyList<string> Roles`
  - `[AttributeUsage(AttributeTargets.Class)] sealed class AllowAnonymousRequestAttribute`
  - `[AttributeUsage(AttributeTargets.Class)] sealed class AllowPendingOnboardingAttribute`
  - `AuthorizationBehavior<TRequest,TResult>` throwing `ApiException.Unauthorized()` /
    `ApiException.Forbidden()` from the existing `Hsm.Application/Errors/ApiException.cs`, so
    status codes and error envelopes stay byte-identical to the frozen contract.

- [ ] **Step 1: Write the failing tests**

```csharp
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;
using Hsm.Application.Errors;

namespace Hsm.Tests.Abstractions;

public class AuthorizationBehaviorTests
{
    [AllowAnonymousRequest]
    private sealed record Public : ICommand<string>;

    private sealed record NeedsAuth : ICommand<string>;

    [RequireRole("admin")]
    private sealed record NeedsAdmin : ICommand<string>;

    [RequireRole("admin")]
    [AllowPendingOnboarding]
    private sealed record AdminDuringOnboarding : ICommand<string>;

    private static AuthorizationBehavior<TRequest, string> Behavior<TRequest>(RequestActor? actor)
        where TRequest : IRequest<string>
    {
        var ambient = new AmbientPrincipal();
        ambient.Set(actor);
        return new AuthorizationBehavior<TRequest, string>(ambient);
    }

    private static RequestHandlerDelegate<string> Reached(out Func<bool> wasReached)
    {
        var hit = false;
        wasReached = () => hit;
        return () => { hit = true; return Task.FromResult("ok"); };
    }

    [Fact]
    public async Task Anonymous_request_runs_without_a_principal()
    {
        var next = Reached(out var reached);
        var result = await Behavior<Public>(actor: null)
            .HandleAsync(new Public(), next, TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.True(reached());
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected_before_the_handler()
    {
        var next = Reached(out var reached);
        var ex = await Assert.ThrowsAsync<ApiException>(() => Behavior<NeedsAuth>(actor: null)
            .HandleAsync(new NeedsAuth(), next, TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(reached());
    }

    [Fact]
    public async Task Wrong_role_is_forbidden_before_the_handler()
    {
        var doctor = new RequestActor("u1", ["doctor"], OnboardingCompleted: true);
        var next = Reached(out var reached);

        var ex = await Assert.ThrowsAsync<ApiException>(() => Behavior<NeedsAdmin>(doctor)
            .HandleAsync(new NeedsAdmin(), next, TestContext.Current.CancellationToken));

        Assert.Equal(403, ex.StatusCode);
        Assert.False(reached());
    }

    [Fact]
    public async Task Required_role_present_runs()
    {
        var admin = new RequestActor("u1", ["admin"], OnboardingCompleted: true);
        var next = Reached(out var reached);

        await Behavior<NeedsAdmin>(admin)
            .HandleAsync(new NeedsAdmin(), next, TestContext.Current.CancellationToken);

        Assert.True(reached());
    }

    [Fact]
    public async Task Pending_onboarding_is_rejected_unless_the_request_opts_in()
    {
        var pending = new RequestActor("u1", ["admin"], OnboardingCompleted: false);

        var ex = await Assert.ThrowsAsync<ApiException>(() => Behavior<NeedsAdmin>(pending)
            .HandleAsync(new NeedsAdmin(), () => Task.FromResult("ok"),
                TestContext.Current.CancellationToken));
        Assert.Equal(403, ex.StatusCode);

        await Behavior<AdminDuringOnboarding>(pending).HandleAsync(
            new AdminDuringOnboarding(), () => Task.FromResult("ok"),
            TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter AuthorizationBehaviorTests`
Expected: FAIL — attributes and behavior do not exist.

- [ ] **Step 3: Write the attributes**

```csharp
namespace Hsm.Application.Abstractions;

/// <summary>Roles permitted to dispatch this request. Absent = authentication only.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireRoleAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

/// <summary>No principal required (sign-in, password reset, provider webhooks).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousRequestAttribute : Attribute;

/// <summary>Dispatchable by a user who has not completed onboarding.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowPendingOnboardingAttribute : Attribute;
```

- [ ] **Step 4: Write `AuthorizationBehavior.cs`**

```csharp
using System.Collections.Concurrent;
using System.Reflection;
using Hsm.Application.Errors;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Authorization is a property of the request, not of the transport. Before this
/// existed, every edge enforced it separately — HTTP endpoints via
/// RequestAuth.GateAsync, in-process UI services via UiServiceGate — and a
/// forgotten call failed open. Here it cannot be forgotten: the same command
/// dispatched from HTTP, a Blazor circuit, or a queued job is gated identically.
/// Failures reuse ApiException so status codes and error envelopes stay
/// byte-identical to the frozen contract.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResult>(ICurrentPrincipal principal)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    private static readonly ConcurrentDictionary<Type, Policy> Policies = new();

    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var policy = Policies.GetOrAdd(typeof(TRequest), static type => Policy.For(type));

        if (policy.AllowAnonymous)
        {
            return next();
        }

        var actor = principal.Actor ?? throw ApiException.Unauthorized();

        if (policy.Roles.Count > 0 && !policy.Roles.Any(actor.IsInRole))
        {
            throw ApiException.Forbidden();
        }

        if (!actor.OnboardingCompleted && !policy.AllowPendingOnboarding)
        {
            throw ApiException.Forbidden();
        }

        return next();
    }

    private sealed record Policy(bool AllowAnonymous, IReadOnlyList<string> Roles, bool AllowPendingOnboarding)
    {
        public static Policy For(Type type) => new(
            AllowAnonymous: type.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null,
            Roles: type.GetCustomAttribute<RequireRoleAttribute>()?.Roles ?? [],
            AllowPendingOnboarding: type.GetCustomAttribute<AllowPendingOnboardingAttribute>() is not null);
    }
}
```

If `ApiException.Unauthorized()` / `Forbidden()` do not already exist with those exact names, add
them beside the existing factories in `src/Hsm.Application/Errors/ApiException.cs`, matching the
status codes and issue codes the frozen contract tests assert.

- [ ] **Step 5: Register it inside `AddHsmPipeline`, after telemetry**

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test tests/Hsm.Tests --filter AuthorizationBehaviorTests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: authorization as a pipeline behavior

Roles declared on the request type and enforced once, so HTTP, Blazor, and
queued dispatch are gated identically. Replaces per-edge gates where a
forgotten call failed open."
```

---

### Task 6: `ValidationBehavior`

**Files:**
- Create: `src/Hsm.Application/Abstractions/IValidator.cs`,
  `Abstractions/Behaviors/ValidationBehavior.cs`
- Modify: `PipelineRegistration.cs`
- Test: `tests/Hsm.Tests/Abstractions/ValidationBehaviorTests.cs`

**Interfaces:**
- Consumes: Tasks 2 and 5.
- Produces:
  - `interface IValidator<in TRequest> { IEnumerable<ValidationFailure> Validate(TRequest request); }`
  - `sealed record ValidationFailure(string Field, string Key, string Message)`
  - Behavior throws `ApiException.Validation(IReadOnlyList<ValidationFailure>)`, whose payload must
    match the frozen `ApiErrorCode.Validation` envelope (per-field constraint keys) already
    asserted by contract tests.

- [ ] **Step 1: Write the failing tests**

```csharp
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;
using Hsm.Application.Errors;

namespace Hsm.Tests.Abstractions;

public class ValidationBehaviorTests
{
    private sealed record Create(string Name) : ICommand<string>;

    private sealed class CreateValidator : IValidator<Create>
    {
        public IEnumerable<ValidationFailure> Validate(Create request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                yield return new ValidationFailure("name", "isNotEmpty", "name should not be empty");
            }
        }
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);

        var result = await behavior.HandleAsync(
            new Create("ok"), () => Task.FromResult("done"), TestContext.Current.CancellationToken);

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task Invalid_request_throws_before_the_handler_with_field_detail()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);
        var reached = false;

        var ex = await Assert.ThrowsAsync<ApiException>(() => behavior.HandleAsync(
            new Create("  "),
            () => { reached = true; return Task.FromResult("done"); },
            TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.StatusCode);
        Assert.False(reached);
        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_validator_registered_is_not_an_error()
    {
        var behavior = new ValidationBehavior<Create, string>([]);

        var result = await behavior.HandleAsync(
            new Create(""), () => Task.FromResult("done"), TestContext.Current.CancellationToken);

        Assert.Equal("done", result);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter ValidationBehaviorTests`
Expected: FAIL — `IValidator`, `ValidationBehavior` do not exist.

- [ ] **Step 3: Write `IValidator.cs`**

```csharp
namespace Hsm.Application.Abstractions;

/// <summary>
/// Business-rule validation for one request type. HTTP-shape validation stays at
/// the edge (Hsm.Api's BodyValidator) — this is for rules the application owns.
/// </summary>
public interface IValidator<in TRequest>
{
    IEnumerable<ValidationFailure> Validate(TRequest request);
}

/// <summary>
/// One failed constraint. Field and Key are wire contract: the frozen error
/// envelope carries per-field constraint keys the frontend maps to copy.
/// </summary>
public sealed record ValidationFailure(string Field, string Key, string Message);
```

- [ ] **Step 4: Write `ValidationBehavior.cs`**

```csharp
using Hsm.Application.Errors;

namespace Hsm.Application.Abstractions.Behaviors;

public sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var failures = validators.SelectMany(v => v.Validate(request)).ToList();
        return failures.Count > 0 ? throw ApiException.Validation(failures) : next();
    }
}
```

Add `ApiException.Validation(IReadOnlyList<ValidationFailure>)` beside the existing factories if
absent, producing the same 400 envelope shape the frozen contract tests assert.

- [ ] **Step 5: Register it after authorization**

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

- [ ] **Step 6: Run and confirm pass**

Run: `dotnet test tests/Hsm.Tests --filter ValidationBehaviorTests`
Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: validation behavior with per-field constraint keys"
```

---

### Task 7: `TransactionBehavior` — commands only

**Files:**
- Create: `src/Hsm.Application/Abstractions/IUnitOfWork.cs`,
  `Abstractions/Behaviors/TransactionBehavior.cs`
- Modify: `PipelineRegistration.cs`, `src/Hsm.Infrastructure/DependencyInjection.cs`
- Test: `tests/Hsm.Tests/Abstractions/TransactionBehaviorTests.cs`,
  `tests/Hsm.Integration.Tests/CommandTransactionTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 5, 6.
- Produces:
  - `interface IUnitOfWork { Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct); }`
  - Implemented in `Hsm.Infrastructure` over the existing `HsmDbContext` execution strategy (the
    existing `IAuthUnitOfWork` becomes a thin alias or is deleted in favour of this).

- [ ] **Step 1: Write the failing unit test**

```csharp
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class TransactionBehaviorTests
{
    private sealed record Write : ICommand<string>;
    private sealed record Read : IQuery<string>;

    private sealed class SpyUnitOfWork : IUnitOfWork
    {
        public int Opened { get; private set; }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            Opened++;
            return work(ct);
        }
    }

    [Fact]
    public async Task Command_opens_a_transaction()
    {
        var uow = new SpyUnitOfWork();
        var behavior = new TransactionBehavior<Write, string>(uow);

        await behavior.HandleAsync(new Write(), () => Task.FromResult("ok"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, uow.Opened);
    }

    [Fact]
    public async Task Query_does_not_open_a_transaction()
    {
        var uow = new SpyUnitOfWork();
        var behavior = new TransactionBehavior<Read, string>(uow);

        await behavior.HandleAsync(new Read(), () => Task.FromResult("ok"),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, uow.Opened);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter TransactionBehaviorTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Write `IUnitOfWork.cs` and `TransactionBehavior.cs`**

```csharp
namespace Hsm.Application.Abstractions;

public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
```

```csharp
namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Innermost behavior. Commands run in a transaction; queries never do —
/// checked at runtime rather than by registering a closed generic, because the
/// dispatcher resolves behaviors for IRequest and both kinds share the chain.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResult>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
        => request is ICommand<TResult>
            ? unitOfWork.ExecuteInTransactionAsync(_ => next(), ct)
            : next();
}
```

- [ ] **Step 4: Implement `IUnitOfWork` in Infrastructure**

Create `src/Hsm.Infrastructure/Persistence/EfUnitOfWork.cs` wrapping
`HsmDbContext.Database.CreateExecutionStrategy()` exactly as the existing
`AuthUnitOfWork` does, and register `services.AddScoped<IUnitOfWork, EfUnitOfWork>();` in
`AddHsmInfrastructure`.

- [ ] **Step 5: Register the behavior last (innermost)**

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
```

- [ ] **Step 6: Write the integration test proving rollback against real Postgres**

```csharp
// tests/Hsm.Integration.Tests/CommandTransactionTests.cs
// Dispatch a command whose handler writes a row and then throws; assert the row
// is absent afterwards. Build the provider with TestServices.Build() (which calls
// AddHsmInfrastructure) plus AddHsmPipeline, following the pattern in
// tests/Hsm.Integration.Tests/ProvingAggregateTests.cs.
```

- [ ] **Step 7: Run all tests**

Run: `dotnet test Hsm.sln`
Expected: PASS — 305 existing plus the new pipeline tests.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat: transaction behavior — commands transactional, queries not

Replaces hand-written ExecuteInTransactionAsync blocks inside handlers with
the innermost pipeline stage, proven to roll back against real Postgres."
```

---

### Task 8: Reference slice — reshape the Users module

This is the **worked example**. Tasks 9–14 repeat this exact procedure; `docs/reference/dotnet-conventions.md`
(Task 25) points here.

**Files:**
- Create: `src/Hsm.Application/Users/Commands/UpdateOwnProfile/{UpdateOwnProfileCommand,UpdateOwnProfileHandler}.cs`
  and the same triple for `ChangeOwnPassword`, `CreateStaffUser`, `ChangeUserRole`
- Create: `src/Hsm.Application/Users/Queries/ListUsers/{ListUsersQuery,ListUsersHandler}.cs`,
  `Queries/GetUser/{GetUserQuery,GetUserHandler}.cs`
- Delete: `src/Hsm.Application/Users/UsersHandlers.cs`
- Modify: `src/Hsm.Web/Users/UserEndpoints.cs`, `src/Hsm.Web/Services/UsersAdminUiService.cs`,
  `src/Hsm.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: Tasks 2–7.
- Produces (later tasks and the UI services dispatch these exact types):
  - `[RequireRole(Roles.Admin)] sealed record CreateStaffUserCommand(string Username, string Email, string FirstName, string FirstLastName, string Role) : ICommand<CreateStaffUserResult>`
  - `[RequireRole(Roles.Admin)] sealed record ChangeUserRoleCommand(Guid UserId, string Role) : ICommand<Unit>`
  - `sealed record UpdateOwnProfileCommand(string? FirstName, string? Email) : ICommand<UserDto>`
  - `sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword) : ICommand<Unit>`
  - `[RequireRole(Roles.Admin)] sealed record ListUsersQuery(int Page, int Limit) : IQuery<ListUsersResult>`
  - `[RequireRole(Roles.Admin)] sealed record GetUserQuery(Guid UserId) : IQuery<UserDto>`

  Self-scoped commands (`UpdateOwnProfile`, `ChangeOwnPassword`) take **no user id** — the actor
  comes from `ICurrentPrincipal`, which removes the "can I edit someone else by passing their id"
  question entirely.

- [ ] **Step 1: Write the failing slice test**

```csharp
// tests/Hsm.Tests/Users/CreateStaffUserCommandTests.cs
// Assert the command carries the admin-only policy — this is the test that
// proves authorization survived the move off the endpoint.
using System.Reflection;
using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Domain.Identity;

namespace Hsm.Tests.Users;

public class CreateStaffUserCommandTests
{
    [Fact]
    public void Is_admin_only()
    {
        var attribute = typeof(CreateStaffUserCommand).GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Contains(Roles.Admin, attribute!.Roles);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter CreateStaffUserCommandTests`
Expected: FAIL — namespace `Hsm.Application.Users.Commands.CreateStaffUser` does not exist.

- [ ] **Step 3: Create the command and handler**

```csharp
// src/Hsm.Application/Users/Commands/CreateStaffUser/CreateStaffUserCommand.cs
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

[RequireRole(Roles.Admin)]
public sealed record CreateStaffUserCommand(
    string Username,
    string Email,
    string FirstName,
    string FirstLastName,
    string Role) : ICommand<CreateStaffUserResult>;

public sealed record CreateStaffUserResult(Guid Id, string Username, string Email);
```

```csharp
// src/Hsm.Application/Users/Commands/CreateStaffUser/CreateStaffUserHandler.cs
using Hsm.Application.Abstractions;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

public sealed class CreateStaffUserHandler(
    IUserStore users,
    IPasswordHasher passwordHasher,
    IStaffWelcomeEmailer emailer)
    : IRequestHandler<CreateStaffUserCommand, CreateStaffUserResult>
{
    public async Task<CreateStaffUserResult> HandleAsync(
        CreateStaffUserCommand request, CancellationToken ct)
    {
        // Body moves verbatim from CreateStaffHandler.HandleAsync in the deleted
        // UsersHandlers.cs, minus its own transaction wrapper (TransactionBehavior
        // owns that now) and minus its role check (AuthorizationBehavior owns that).
        // The temp-password hash is computed BEFORE any write, as the frozen
        // ResetPasswordHandler does — bcrypt inside a transaction holds a
        // connection for ~100ms of pure CPU.
        throw new NotImplementedException("move the body from CreateStaffHandler");
    }
}
```

> The `NotImplementedException` above is a **marker for the implementer to move an existing
> body**, not a placeholder for new logic. The body already exists and is already
> contract-tested; it must arrive unchanged apart from the two removals noted.

- [ ] **Step 4: Repeat for the other five use cases in this module**

| Deleted handler | New request type | Kind | Policy |
|---|---|---|---|
| `UpdateOwnProfileHandler` | `UpdateOwnProfileCommand` | command | authenticated; actor from principal |
| `ChangeOwnPasswordHandler` | `ChangeOwnPasswordCommand` | command | authenticated; actor from principal |
| `CreateStaffHandler` | `CreateStaffUserCommand` | command | `[RequireRole(Roles.Admin)]` |
| `ChangeUserRoleHandler` | `ChangeUserRoleCommand` | command | `[RequireRole(Roles.Admin)]` |
| `ListUsersHandler` | `ListUsersQuery` | query | `[RequireRole(Roles.Admin)]` |
| `GetUserHandler` | `GetUserQuery` | query | `[RequireRole(Roles.Admin)]` |

`ListUsersResult` drops the `TotalPages` field — `ApiEnvelope.Pagination` computes it (established
by the Phase 4 consolidation pass).

- [ ] **Step 5: Add the validator for `CreateStaffUser`**

```csharp
// src/Hsm.Application/Users/Commands/CreateStaffUser/CreateStaffUserValidator.cs
using Hsm.Application.Abstractions;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

public sealed class CreateStaffUserValidator : IValidator<CreateStaffUserCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateStaffUserCommand request)
    {
        // Frozen behavior: patient and family roles are rejected for staff creation.
        if (!RoleCatalog.IsAssignableToStaff(request.Role))
        {
            yield return new ValidationFailure("role", "isIn", "role must be a staff role");
        }
    }
}
```

Keep the exact constraint key (`isIn`) and message the frozen contract tests assert. If
`RoleCatalog.IsAssignableToStaff` does not exist, add it beside the existing role list rather than
inlining the check.

- [ ] **Step 6: Register the slice and update the two call sites**

In `DependencyInjection.cs`, replace the six `AddScoped<XHandler>()` lines with
`AddScoped<IRequestHandler<TRequest,TResult>, THandler>()` registrations plus
`AddScoped<IValidator<CreateStaffUserCommand>, CreateStaffUserValidator>()`.

`src/Hsm.Web/Users/UserEndpoints.cs` — each endpoint becomes a dispatch. Note the guard chain is
gone; the endpoint still authenticates (transport work) but no longer authorizes:

```csharp
users.MapPost("/staff", async (
    CreateStaffUserCommand command, IDispatcher dispatcher, CancellationToken ct) =>
        ApiEnvelope.Created(await dispatcher.Send(command, ct)));
```

`src/Hsm.Web/Services/UsersAdminUiService.cs` — same change, dispatching instead of calling
handlers, and its `UiServiceGate` call removed (the pipeline gates it now).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test Hsm.sln`
Expected: PASS. Every users/settings contract test must pass **unmodified** — including the
403-for-non-admin and no-self-escalation cases, which now prove the pipeline enforces what the
edge used to.

- [ ] **Step 8: Verify format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: Users module as command/query slices

Six handlers become four commands and two queries, each in its own folder
with its policy declared on the request type. Self-scoped commands take no
user id — the actor comes from ICurrentPrincipal. Reference slice for the
remaining modules."
```

---

### Tasks 9–14: Reshape the remaining modules

Each task follows **Task 8's procedure exactly** (failing policy test → create slice folders →
move handler bodies verbatim minus transaction and role checks → register → update call sites →
full suite → format → commit). Run them **one module per task**, never batched: the contract
suite is the only thing standing between this refactor and a silent behavior change.

The tables below are the complete conversion list. Nothing is left to invent.

### Task 9: Settings

Source: `src/Hsm.Application/Settings/SettingsHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `GetSettingsHandler` | `GetSettingsQuery` | query | `[RequireRole(Roles.Admin)]` |
| `UpdateSettingsHandler` | `UpdateSettingsCommand` | command | `[RequireRole(Roles.Admin)]` |
| `ListSettingsAuditHandler` | `ListSettingsAuditQuery` | query | `[RequireRole(Roles.Admin)]` |

Module notes: secret masking, blank-secret-means-unchanged, unknown-key-ignored, and one audit row
per *effective* change all move unchanged. The audit row's actor now comes from
`ICurrentPrincipal` rather than a parameter.

### Task 10: Auth

Sources: `AuthHandlers.cs`, `AccountRecoveryHandlers.cs`, `IntegrationAccountAdminHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `LoginHandler` | `LoginCommand` | command | `[AllowAnonymousRequest]` |
| `SignupHandler` | `SignupCommand` | command | `[AllowAnonymousRequest]` |
| `RefreshHandler` | `RefreshTokensCommand` | command | `[AllowAnonymousRequest]` (the refresh token is the credential) |
| `LogoutHandler` | `LogoutCommand` | command | authenticated, `[AllowPendingOnboarding]` |
| `SignupIntegrationHandler` | `SignupIntegrationCommand` | command | `[RequireRole(Roles.Admin)]` |
| `LogoutIntegrationHandler` | `LogoutIntegrationCommand` | command | authenticated |
| `CompleteOnboardingHandler` | `CompleteOnboardingCommand` | command | authenticated, `[AllowPendingOnboarding]` |
| `ForgotPasswordHandler` | `ForgotPasswordCommand` | command | `[AllowAnonymousRequest]` |
| `ResetPasswordHandler` | `ResetPasswordCommand` | command | `[AllowAnonymousRequest]` |
| `RecoverUsernameHandler` | `RecoverUsernameCommand` | command | `[AllowAnonymousRequest]` |
| `ListIntegrationAccountsHandler` | `ListIntegrationAccountsQuery` | query | `[RequireRole(Roles.Admin)]` |
| `IssueIntegrationTokensHandler` | `IssueIntegrationTokensCommand` | command | `[RequireRole(Roles.Admin)]` |
| `RevokeIntegrationTokensHandler` | `RevokeIntegrationTokensCommand` | command | `[RequireRole(Roles.Admin)]` |

Module notes — **highest-risk task in the plan**:
- The per-account rate limit (5 reset requests/hour → 429) and the enumeration-safe responses are
  frozen behavior with contract tests. They stay in the handlers; do not convert them to validators
  (a validator failure is a 400, not a 429).
- The SHA-256-pre-digest before bcrypt for refresh tokens must survive — it exists because the
  frozen bcrypt-of-JWT accepted any same-user token past 72 bytes, defeating rotation.
- `[AllowAnonymousRequest]` on the three recovery commands and login/signup is what keeps them
  reachable; getting one wrong locks users out and the contract tests will say so.
- Two token stores stay separate.

### Task 11: Templates

Source: `TemplatesHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `ListTemplatesHandler` | `ListTemplatesQuery` | query | authenticated |
| `GetTemplateHandler` | `GetTemplateQuery` | query | authenticated |
| `CreateTemplateHandler` | `CreateTemplateCommand` | command | `[RequireRole(Roles.Admin)]` |
| `UpdateTemplateHandler` | `UpdateTemplateCommand` | command | `[RequireRole(Roles.Admin)]` |
| `DeleteTemplateHandler` | `DeleteTemplateCommand` | command | `[RequireRole(Roles.Admin)]` |
| `ValidateTemplateHandler` | `ValidateTemplateQuery` | query | authenticated |
| `DraftRenderHandler` | `DraftRenderQuery` | query | authenticated |

Module notes: `ValidateTemplate` and `DraftRender` are **queries** — they compute without
persisting, so they must not open a transaction. The in-use deletion check stays in the handler and
keeps raising the frozen 409 (not a validation 400).

### Task 12: Coms

Sources: `ComsHandlers.cs`, `ComsJobHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `SendEmailHandler` | `SendEmailCommand` | command | authenticated |
| `ListEmailBatchesHandler` | `ListEmailBatchesQuery` | query | authenticated |
| `GetEmailBatchHandler` | `GetEmailBatchQuery` | query | authenticated |
| `ResendEmailBatchHandler` | `ResendEmailBatchCommand` | command | authenticated |
| `ListEmailRecipientsHandler` | `ListEmailRecipientsQuery` | query | authenticated |
| `GetEmailRecipientHandler` | `GetEmailRecipientQuery` | query | authenticated |
| `ResendEmailRecipientHandler` | `ResendEmailRecipientCommand` | command | authenticated |
| `ReceiveWebhookHandler` | `ReceiveWebhookCommand` | command | `[AllowAnonymousRequest]` — signature is the credential |
| `SendEmailJobHandler` | `DispatchEmailBatchCommand` | command | `[JobName("coms.send-email")]`, authenticated |
| `ProcessWebhookJobHandler` | `ProcessWebhookEventCommand` | command | `[JobName("coms.process-webhook")]`, `[AllowAnonymousRequest]` |

Module notes: the two job handlers become commands carrying `[JobName]`, which is what lets Task 21
put them on the queue. HMAC-SHA1 verification stays exactly as strong. Duplicate-event idempotency
stays in the handler.

### Task 13: Docs

Sources: `DocsHandlers.cs`, `DocsJobHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `ListDocumentsHandler` | `ListDocumentsQuery` | query | authenticated |
| `GetDocumentHandler` | `GetDocumentQuery` | query | authenticated |
| `GetDocumentUrlHandler` | `GetDocumentUrlQuery` | query | authenticated |
| `PresignDocumentsHandler` | `PresignDocumentsQuery` | query | authenticated |
| `GenerateDocumentHandler` | `GenerateDocumentCommand` | command | authenticated |
| `UploadDocumentsHandler` | `UploadDocumentsCommand` | command | authenticated |
| `DeleteDocumentHandler` | `DeleteDocumentCommand` | command | authenticated |
| `GenerateDocumentJobHandler` | `RenderDocumentCommand` | command | `[JobName("docs.render")]`, authenticated |

Module notes: presign is a **query** (signing is local, no round trip, no persistence). Uploads keep
streaming — do not reintroduce the `MemoryStream`+`ToArray` triple copy the consolidation pass
removed. `GenerateDocumentCommand` enqueues and returns `{documentId, jobId}`; `RenderDocumentCommand`
does the work.

### Task 14: Clinical

Source: `PatientHandlers.cs`

| Handler | Request type | Kind | Policy |
|---|---|---|---|
| `GetPatientHandler` | `GetPatientQuery` | query | `[RequireRole(...ClinicalStaffRoles)]` |
| `SearchPatientsHandler` | `SearchPatientsQuery` | query | `[RequireRole(...ClinicalStaffRoles)]` |
| `CreatePatientHandler` | `CreatePatientCommand` | command | `[RequireRole(...ClinicalStaffRoles)]` |

Module notes: the clinical roles gate moves from `FhirEndpoints.GateAsync` onto the three request
types — list the same roles the frozen `@FhirController` enforced (doctor, nurse, technician,
therapist, pharmacist, with admin bypass). An integration-only bearer must still get 403; that
contract test is the proof. FHIR responses keep bypassing the success envelope and keep returning
`OperationOutcome` on error.

Also in this task: `GetSystemStatusHandler` (`src/Hsm.Application/System/`) becomes
`GetSystemStatusQuery` with `[AllowAnonymousRequest]`.

---

### Task 15: Remove the last edge gates and prove the pipeline holds

**Files:**
- Modify: every `*Endpoints.cs` under `src/Hsm.Web/` (remove `RequestAuth.GateAsync` role
  arguments, keep authentication), `src/Hsm.Web/Services/UiServiceGate.cs` (delete),
  all `src/Hsm.Web/Services/*UiService.cs`
- Modify: `src/Hsm.Web/Auth/RequestAuth.cs` — keep `AuthenticateAsync`, delete `RequireRoles` and
  `RequireOnboardingCompletedAsync`
- Test: `tests/Hsm.Contract.Tests/Shell/PipelineAuthorizationTests.cs`

**Interfaces:**
- Consumes: `ICurrentPrincipal` (Task 3), all reshaped slices (Tasks 8–14).
- Produces: a host-side `HttpCurrentPrincipal : ICurrentPrincipal` reading the authenticated
  principal from `IHttpContextAccessor`, registered scoped in both HTTP hosts.

- [ ] **Step 1: Write the failing test — in-process dispatch must be gated with no edge present**

```csharp
// A doctor-session UI service call to an admin-only command must be refused by
// the pipeline alone. Before this plan, UiServiceGate did that check manually;
// this test proves removing it did not open a hole.
```

Use the `ContractApiFactory` pattern in `tests/Hsm.Contract.Tests/Support/`: seed a doctor, resolve
`IDispatcher` from the host's scope with the doctor installed as principal, dispatch
`ListUsersQuery`, assert `ApiException` with 403.

- [ ] **Step 2: Run and confirm it fails** (it passes for the wrong reason while `UiServiceGate`
  still exists — so delete the gate first, watch the test fail, then confirm the pipeline catches it)

- [ ] **Step 3: Implement `HttpCurrentPrincipal` and register it**

- [ ] **Step 4: Strip the gates**

Delete `UiServiceGate` and every call to it; remove role arguments from endpoint
`RequestAuth.GateAsync` calls, leaving authentication.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test Hsm.sln`
Expected: PASS. Every existing 401/403 test must still pass — that is the whole verification.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: authorization enforced only by the pipeline

Edge gates removed from endpoints and UI services; every existing 401/403
contract test passes unchanged, and a new test proves in-process dispatch
is refused for a wrong-role principal with no edge check present."
```

---

### Task 16: Fold the screens into `Hsm.Web`, replace the compiler boundary with a test

**Files:**
- Move: `src/Hsm.Web.Components/{Pages,Layout}/**`, `Routes.razor`, `_Imports.razor`,
  `HsmTheme.cs`, `SystemStatusPanel.razor` → `src/Hsm.Web/`
- Delete: `src/Hsm.Web.Components/` and its solution entry
- Modify: `src/Hsm.Web/Hsm.Web.csproj` (add the MudBlazor package reference),
  `src/Hsm.Web/Program.cs` (drop `AddAdditionalAssemblies`)
- Rewrite: `tests/Hsm.Architecture.Tests/ClientIsolationBoundaryTests.cs` →
  `tests/Hsm.Tests/Architecture/ScreenIsolationTests.cs`
- Rename: `tests/Hsm.Web.Components.Tests` → `tests/Hsm.Web.Tests`

**Interfaces:**
- Consumes: nothing new.
- Produces: namespaces `Hsm.Web.Components.*` become `Hsm.Web.*`. Every test file referencing the
  old namespace updates.

- [ ] **Step 1: Write the new boundary test — two independent mechanisms**

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace Hsm.Tests.Architecture;

/// <summary>
/// Option B: screens live inside Hsm.Web, which references Infrastructure for
/// DI — so the compiler can no longer make a shortcut impossible (it did while
/// screens lived in their own project). These two tests are the replacement.
/// Source scanning catches the realistic violation (@inject / @using, including
/// a global smuggled through _Imports.razor); reflection catches anything that
/// survives a folder rename.
/// </summary>
public class ScreenIsolationTests
{
    private static readonly string[] Forbidden =
    [
        "Hsm.Application", "Hsm.Infrastructure", "Microsoft.EntityFrameworkCore",
    ];

    [Fact]
    public void No_razor_file_references_a_forbidden_namespace()
    {
        var screens = Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "Hsm.Web"), "*.razor", SearchOption.AllDirectories);

        var violations = new List<string>();
        foreach (var file in screens)
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("@using", StringComparison.Ordinal)
                    && !trimmed.StartsWith("@inject", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Forbidden.Any(f => trimmed.Contains(f, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetFileName(file)}: {trimmed}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void No_component_injects_a_forbidden_type()
    {
        var componentTypes = typeof(Hsm.Web.Routes).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract);

        var violations = componentTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public
                                             | BindingFlags.NonPublic))
            .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
            .Where(p => Forbidden.Any(f =>
                (p.PropertyType.Namespace ?? string.Empty).StartsWith(f, StringComparison.Ordinal)))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}: {p.PropertyType.Name}")
            .ToList();

        Assert.Empty(violations);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hsm.sln")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Hsm.sln not found.");
    }
}
```

- [ ] **Step 2: Move the files and delete the project**

```bash
git mv src/Hsm.Web.Components/Pages src/Hsm.Web/Pages
git mv src/Hsm.Web.Components/Layout src/Hsm.Web/Layout
git mv src/Hsm.Web.Components/Routes.razor src/Hsm.Web/Routes.razor
git mv src/Hsm.Web.Components/HsmTheme.cs src/Hsm.Web/HsmTheme.cs
git mv src/Hsm.Web.Components/SystemStatusPanel.razor src/Hsm.Web/SystemStatusPanel.razor
dotnet sln Hsm.sln remove src/Hsm.Web.Components/Hsm.Web.Components.csproj
```

Merge `src/Hsm.Web.Components/_Imports.razor` into `src/Hsm.Web/Host/_Imports.razor`, move the
MudBlazor `PackageReference` into `Hsm.Web.csproj`, delete the leftover directory, and rename
namespaces `Hsm.Web.Components` → `Hsm.Web` across `src/` and `tests/`.

- [ ] **Step 3: Drop `AddAdditionalAssemblies` from `Program.cs`** — pages are in the host assembly now.

- [ ] **Step 4: Rename the component test project**

```bash
git mv tests/Hsm.Web.Components.Tests tests/Hsm.Web.Tests
```

Rename the csproj, assembly name, and root namespace; update the solution entry.

- [ ] **Step 5: Observe the new boundary test RED before trusting it**

Add `@inject Microsoft.EntityFrameworkCore.DbContext Db` to `src/Hsm.Web/Pages/Home.razor`.
Run: `dotnet test tests/Hsm.Tests --filter ScreenIsolationTests`
Expected: **FAIL** on both tests. Then remove the line and confirm both pass. A boundary test that
has never failed proves nothing — this is the same discipline the compiler-enforced version got.

- [ ] **Step 6: Run the full suite, format, commit**

```bash
dotnet test Hsm.sln && dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: screens live in Hsm.Web, boundary held by a test

Option B: Hsm.Web.Components is deleted and its 17 files move into the host.
The client-isolation constraint is now enforced by a source scan plus
reflection over injected component members, observed red before trusted.
Mobile/WASM will require extracting these screens; that cost is accepted."
```

---

### Task 17: Split `Hsm.Api` out of `Hsm.Web`

**Files:**
- Create: `src/Hsm.Api/` (project, `Program.cs`), moving from `src/Hsm.Web/`:
  `Api/{ApiEnvelope,Validation,ApiErrorHandling,ErrorStatusCodes}.cs`,
  `Auth/{AuthEndpoints,CsrfProtection}.cs`,
  `{Users,Settings,Templates,Coms,Docs,Fhir,Health}/*Endpoints.cs`
- Create: `src/Hsm.Contracts/Auth/AuthCookiePolicy.cs`
- Modify: `src/Hsm.Web/Auth/{AuthCookies,RequestAuth}.cs` (keep per-host, read policy from contracts),
  `tests/Hsm.Contract.Tests/Support/ContractApiFactory.cs` (target `Hsm.Api`)

**Interfaces:**
- Consumes: everything from Tasks 8–15.
- Produces:
  - `static class AuthCookiePolicy` in `Hsm.Contracts` — `AccessTokenName`, `RefreshTokenName`,
    `RefreshPath`, `AccessMaxAge`, `RefreshMaxAge`, `AccessSameSite`, `RefreshSameSite`. The exact
    values the frozen contract tests assert; both hosts read them.
  - `Hsm.Api.Program` (partial, for `WebApplicationFactory<Program>`).

- [ ] **Step 1: Create the project and reference graph**

```bash
dotnet new web -o src/Hsm.Api -n Hsm.Api
dotnet sln Hsm.sln add src/Hsm.Api/Hsm.Api.csproj
dotnet add src/Hsm.Api reference src/Hsm.Application src/Hsm.Infrastructure src/Hsm.Contracts
```

Strip the template's `TargetFramework`/`Nullable`/`ImplicitUsings`; delete the sample endpoint.

- [ ] **Step 2: Extract the shared cookie policy into contracts**

Move the cookie names, paths, `SameSite` modes and max-ages out of
`src/Hsm.Web/Auth/AuthCookies.cs` into `src/Hsm.Contracts/Auth/AuthCookiePolicy.cs`. Each host keeps
its own ~30-line extension method applying them to its `HttpResponse`. This is the whole of the
cross-host overlap — the JWT crypto is already shared through `IAuthTokenCodec`.

- [ ] **Step 3: Move the endpoints and API plumbing**

`git mv` the files listed above into `src/Hsm.Api/`, renaming namespaces `Hsm.Web.*` → `Hsm.Api.*`.
`Hsm.Web` keeps: the Blazor host, the screens, `Services/*UiService.cs`, and
`Auth/{HsmCookieAuthenticationHandler,HsmAuthenticationStateProvider,AuthPrincipalClaims,AuthCookies,RequestAuth}.cs`.

- [ ] **Step 4: Write `src/Hsm.Api/Program.cs`**

Mirror the current `Hsm.Web/Program.cs` minus everything Blazor: `AddHsmInfrastructure`,
`AddHsmPipeline`, OpenTelemetry (service name `hsm-api`), rate limiter, CSRF, error middleware,
`HttpCurrentPrincipal`, and the endpoint `Map*` calls. End with the partial-class declaration so
`WebApplicationFactory<Program>` works.

- [ ] **Step 5: Strip REST from `Hsm.Web/Program.cs`**

Remove every `Map*Endpoints()` call and the JSON envelope/validation middleware. Keep the Blazor
pipeline, the `/_blazor` CSRF exemption, and the auth scheme.

- [ ] **Step 6: Retarget the contract tests**

In `tests/Hsm.Contract.Tests/Support/ContractApiFactory.cs`, change
`WebApplicationFactory<Hsm.Web.Program>` to `WebApplicationFactory<Hsm.Api.Program>` and update the
project reference. The shell tests in `tests/Hsm.Contract.Tests/Shell/` keep targeting `Hsm.Web`.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test Hsm.sln`
Expected: PASS, unchanged counts. Every frozen-contract test now runs against `Hsm.Api`; the
route-closure test asserts `Hsm.Api`'s endpoint set matches the snapshot.

- [ ] **Step 8: Verify independence by hand**

```bash
# in the container, two terminals
ASPNETCORE_URLS=http://0.0.0.0:5001 dotnet run --project src/Hsm.Api
ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet run --project src/Hsm.Web
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5001/v1/health   # 200
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5000/v1/health   # 404 — Web serves no REST
```

Then kill `Hsm.Api` and confirm an authenticated Blazor page still loads — that is the
independent-deploy property this task exists for.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "refactor: split Hsm.Api out of Hsm.Web

Two doors onto one core: Hsm.Api serves external integrations, Hsm.Web serves
staff through in-process handler calls. Cookie policy moves to contracts;
plumbing stays per-host (~30 lines each) since the JWT crypto is already
shared through IAuthTokenCodec. No Hsm.Hosting project."
```

---

### Task 18: Consolidate the test projects

**Files:**
- Delete: `tests/Hsm.Domain.Tests/`, `tests/Hsm.Application.Tests/` (both contain zero files)
- Move: `tests/Hsm.Architecture.Tests/*` → `tests/Hsm.Tests/Architecture/`
- Modify: `.github/workflows/pr-validation.yml`

- [ ] **Step 1: Delete the empty projects and fold in the architecture tests**

```bash
dotnet sln Hsm.sln remove tests/Hsm.Domain.Tests/Hsm.Domain.Tests.csproj
dotnet sln Hsm.sln remove tests/Hsm.Application.Tests/Hsm.Application.Tests.csproj
rm -rf tests/Hsm.Domain.Tests tests/Hsm.Application.Tests
git mv tests/Hsm.Architecture.Tests/PortPurityTests.cs tests/Hsm.Tests/Architecture/
# ScreenIsolationTests already lives there from Task 16
dotnet sln Hsm.sln remove tests/Hsm.Architecture.Tests/Hsm.Architecture.Tests.csproj
```

`Hsm.Tests` needs references to `Hsm.Api`, `Hsm.Web`, `Hsm.Contracts`, `Hsm.Domain`,
`Hsm.Application`, `Hsm.Infrastructure` for the architecture assertions.

- [ ] **Step 2: Simplify the CI filter**

`Hsm.Tests` is now the only infrastructure-free project, so the unit job names projects explicitly
instead of filtering by trait:

```yaml
      - name: Run unit tests
        run: dotnet test tests/Hsm.Tests --configuration Release
      - name: Run component tests
        run: dotnet test tests/Hsm.Web.Tests --configuration Release
```

Retire the `Infra!=true` filter and the `[Trait("Infra", ...)]` attributes it depended on.

- [ ] **Step 3: Run everything and confirm the split**

Run: `dotnet test tests/Hsm.Tests` with **no containers running** — expected PASS.
Then `dotnet test Hsm.sln` with infrastructure up — expected PASS.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test: four test projects, split by what they need to run

Deletes two empty projects, folds architecture tests into Hsm.Tests, and
replaces the accumulated CI trait filter with explicit project names."
```

---

### Task 19: Redis Streams job queue

**Files:**
- Create: `src/Hsm.Application/Ports/IJobQueue.cs`,
  `src/Hsm.Application/Abstractions/JobNameAttribute.cs`,
  `src/Hsm.Infrastructure/Jobs/{RedisStreamJobQueue,JobNameRegistry,JobEnvelope,DelayedJobPump}.cs`
- Delete: `src/Hsm.Infrastructure/Jobs/ChannelJobQueue.cs`
- Modify: `Directory.Packages.props` (add `StackExchange.Redis`),
  `src/Hsm.Infrastructure/DependencyInjection.cs`
- Test: `tests/Hsm.Integration.Tests/JobQueueDurabilityTests.cs`

**Interfaces:**
- Consumes: `ICommand<Unit>` (Task 2), `RequestActor` (Task 3), the `[JobName]`-carrying commands
  from Tasks 12–13.
- Produces:
  - `[AttributeUsage(AttributeTargets.Class)] sealed class JobNameAttribute(string name) : Attribute { public string Name { get; } }`
  - `interface IJobQueue { Task<string> EnqueueAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : ICommand<Unit>; }`
  - `interface IJobConsumer { Task<int> DrainOnceAsync(string queue, int max, CancellationToken ct); }`
    — the worker's entry point, also what the tests drive.
  - `sealed record JobEnvelope(string JobName, string Payload, RequestActor? Actor, int Attempt)`

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Hsm.Infrastructure package StackExchange.Redis
```

- [ ] **Step 2: Write the failing durability test — this is the defect being fixed**

```csharp
// tests/Hsm.Integration.Tests/JobQueueDurabilityTests.cs
// Against real Redis at redis:6379.

[Fact]
public async Task Job_enqueued_before_a_consumer_restart_still_runs()
{
    // 1. Build provider A, enqueue a command, dispose provider A WITHOUT consuming.
    //    (Disposal is the "restart" — the in-memory Channel implementation loses
    //    the job here, which is exactly the regression this task removes.)
    // 2. Build provider B against the same Redis, drain the queue.
    // 3. Assert the command's handler ran exactly once.
}

[Fact]
public async Task Two_consumers_do_not_both_process_one_job()
{
    // Enqueue one job, drain concurrently from two consumers in the same group,
    // assert the handler ran exactly once (Redis consumer-group semantics).
}

[Fact]
public async Task Job_failing_every_attempt_lands_in_the_dead_letter_stream()
{
    // Handler always throws; drain until attempts are exhausted; assert the
    // envelope is present in the dead-letter stream and absent from the live one.
}

[Fact]
public async Task Queued_command_authorizes_with_the_enqueuing_actor()
{
    // Enqueue an admin-only command as an admin; drain; assert it succeeds.
    // Enqueue as a doctor; drain; assert the pipeline refuses it.
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/Hsm.Integration.Tests --filter JobQueueDurabilityTests`
Expected: FAIL — `IJobQueue`, `RedisStreamJobQueue` do not exist.

- [ ] **Step 4: Implement the queue**

- `JobNameRegistry` scans `Hsm.Application` once at startup for `[JobName]` types and builds
  `name → Type` and `Type → name` maps. **Never serialize assembly-qualified type names** — they
  break on rename and are a deserialization footgun.
- `RedisStreamJobQueue.EnqueueAsync` serializes `JobEnvelope` (System.Text.Json) and `XADD`s it to
  `hsm:jobs:{queue}`, returning the stream entry id as the job id.
- `DrainOnceAsync` uses `XREADGROUP` on group `hsm-workers` with a per-process consumer name,
  deserializes, installs `Actor` into the scoped `AmbientPrincipal`, dispatches through
  `IDispatcher`, then `XACK`. On failure: if attempts remain, `ZADD hsm:jobs:{queue}:delayed` scored
  with `now + RetryBaseDelay * 2^(attempt-1)`; otherwise `XADD hsm:jobs:{queue}:dead`. `XACK` either
  way so the message leaves the pending list.
- `DelayedJobPump` is a `PeriodicTimer` loop moving due entries from the delayed sorted set back
  into the stream.
- `XAUTOCLAIM` on startup reclaims messages left pending by a dead consumer.
- Per-queue configuration preserves the frozen retry posture: **coms** 5 attempts / 5s base and
  **one consumer** (frozen resend ordering depends on serial processing — established by the Phase 4
  consolidation pass); **docs** 3 attempts / 1s initial / 2s base, 4 consumers.
- Delivery is at-least-once, matching frozen BullMQ; handlers stay idempotent.

- [ ] **Step 5: Register and delete the channel implementation**

Replace `AddChannelJobQueue<...>` calls in `DependencyInjection.cs` with the Redis registration;
delete `Jobs/ChannelJobQueue.cs` and the `IComsJobDispatcher`/`IDocsJobDispatcher` ports in favour
of the single `IJobQueue`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Hsm.Integration.Tests`
Expected: PASS, including the four new durability tests.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "fix: durable Redis Streams job queue replaces the in-memory channel

The channel queue lost every queued send and generation on restart, and with
N host instances each held its own invisible queue — a regression from the
frozen BullMQ behavior. Streams with consumer groups give at-least-once
delivery with XACK, XAUTOCLAIM recovery, a delayed set for backoff, and a
dead-letter stream. Queued commands carry their enqueuing actor, so they
authorize exactly as their HTTP counterparts do."
```

---

### Task 20: `Hsm.Worker` becomes the real consumer, with scheduling

**Files:**
- Modify: `src/Hsm.Worker/Program.cs`; delete `src/Hsm.Worker/Worker.cs` (the 59-line heartbeat)
- Create: `src/Hsm.Worker/JobConsumerService.cs`,
  `src/Hsm.Worker/Scheduling/{Scheduler,ScheduleRegistry}.cs`
- Test: `tests/Hsm.Integration.Tests/WorkerEndToEndTests.cs`

**Interfaces:**
- Consumes: `IJobQueue`, `IJobConsumer` (Task 19).
- Produces:
  - `sealed record ScheduledJob(string Name, TimeSpan Interval, Func<IDispatcher, CancellationToken, Task> Run)`
  - `ScheduleRegistry.Add(ScheduledJob)` — the one place recurring work is declared.

- [ ] **Step 1: Write the failing end-to-end test**

```csharp
// Enqueue a DispatchEmailBatchCommand, run one JobConsumerService drain cycle,
// assert the batch reached SENT and the capturing transport recorded the send —
// with no HTTP host running at all.
```

- [ ] **Step 2: Run and confirm failure**

- [ ] **Step 3: Implement `JobConsumerService`**

A `BackgroundService` per configured queue calling `DrainOnceAsync` in a loop, honouring the
per-queue consumer count (coms 1, docs 4). One DI scope per job so `AmbientPrincipal`,
`HsmDbContext`, and the pipeline are correctly scoped.

- [ ] **Step 4: Implement the scheduler**

A `PeriodicTimer` per registered `ScheduledJob`. Before firing, take a Redis lease:
`SET hsm:sched:{name} {instanceId} NX PX {interval*0.9}`. Only the winner dispatches, so N workers
fire a schedule once. `ScheduleRegistry` starts empty with a commented example — a durable
exactly-once scheduler is out of scope and recorded as such.

- [ ] **Step 5: Rewrite `Program.cs`**

`AddHsmInfrastructure` + `AddHsmPipeline` + `JobConsumerService` + `Scheduler` + OpenTelemetry
(service name `hsm-worker`). Delete `Worker.cs`.

- [ ] **Step 6: Verify hosts no longer consume**

Confirm neither `Hsm.Api` nor `Hsm.Web` registers a consumer — they enqueue only. Grep for
`JobConsumerService` outside `Hsm.Worker`; expect no hits.

- [ ] **Step 7: Run everything, format, commit**

```bash
git add -A && git commit -m "feat: worker consumes the durable queue and runs scheduled work

Replaces the 59-line heartbeat. Background work leaves the request-serving
hosts entirely; schedules lease through Redis SET NX PX so N workers fire a
recurring job once."
```

---

### Task 21: First EF migrations

**Files:**
- Create: `src/Hsm.Infrastructure/Migrations/**`, `src/Hsm.Api/MigrateCommand.cs`
- Modify: `tests/Hsm.Integration.Tests/TestServices.cs`,
  `tests/Hsm.Contract.Tests/Support/ContractApiFactory.cs`,
  `.github/workflows/pr-validation.yml`

**Interfaces:**
- Consumes: `HsmDbContext`.
- Produces: `dotnet run --project src/Hsm.Api -- --migrate` applies migrations and exits.

- [ ] **Step 1: Add the EF tooling and generate the baseline**

```bash
dotnet add src/Hsm.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef   # if absent
dotnet ef migrations add Initial \
  --project src/Hsm.Infrastructure \
  --startup-project src/Hsm.Api \
  --output-dir Migrations
```

- [ ] **Step 2: Prove the migration equals the model**

```bash
dotnet ef database update --project src/Hsm.Infrastructure --startup-project src/Hsm.Api
dotnet ef migrations has-pending-model-changes --project src/Hsm.Infrastructure --startup-project src/Hsm.Api
```

Expected: no pending model changes. That equivalence is the test — the migrated schema must match
what `EnsureCreated` produces today, including the `citext` extension, the filtered unique indexes
on `users`, the `jsonb` columns on `patient`, and `uq_patient_identifier_system_value`.

- [ ] **Step 3: Add the `--migrate` entry point**

In `src/Hsm.Api/Program.cs`, before building the app: if `args` contains `--migrate`, resolve
`HsmDbContext`, call `Database.MigrateAsync()`, and return. Never migrate on normal boot — two
request-serving hosts racing `Migrate()` is a known failure mode.

- [ ] **Step 4: Switch the test fixtures from `EnsureCreated` to `Migrate`**

Both `TestServices` and `ContractApiFactory` call `MigrateAsync()` instead of
`EnsureCreatedAsync()`, so every test run exercises the real schema path.

- [ ] **Step 5: Add the CI drift check**

A step running `dotnet ef migrations has-pending-model-changes` that fails the build when the model
changes without a migration.

- [ ] **Step 6: Run the full suite against a migrated database, then commit**

```bash
git add -A && git commit -m "feat: initial EF migrations and an explicit migrate step

The repository had no migrations at all — tests created schema with
EnsureCreated, so there was no way to create or evolve a real database.
Migrations are applied by 'dotnet run --project src/Hsm.Api -- --migrate',
never on host boot, and a CI drift check fails on an un-migrated model."
```

---

### Task 22: Dockerfiles, compose services, CI

**Files:**
- Create: `docker/api.Dockerfile`, `docker/web.Dockerfile`, `docker/worker.Dockerfile`
- Modify: `docker/docker-compose.yaml`, `.github/workflows/{build,CICD,deploy}.yml`

- [ ] **Step 1: Write the three Dockerfiles**

Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0-noble` to restore and `dotnet publish`, then
`mcr.microsoft.com/dotnet/aspnet:10.0-noble` for `api`/`web` and
`mcr.microsoft.com/dotnet/runtime:10.0-noble` for `worker`. Copy `Directory.*.props` and every
`.csproj` first so restore layers cache. Run as a non-root user.

> `build.yml` currently references `./docker/app.Dockerfile`, which does not exist — deploy is
> broken today. This task is what fixes it.

- [ ] **Step 2: Add the compose app services**

`hsm-app-api` (port 5001), `hsm-app-web` (5000), `hsm-app-worker` (no port), each with
`env_file: ../secrets.env`, `depends_on` the infrastructure they need, on `hsm-app-network`. Leave
them **out** of the devcontainer's `runServices` — the local loop keeps using `dotnet run` for fast
rebuilds; these exist for production-shaped runs.

- [ ] **Step 3: Update the workflows**

`build.yml` takes `APP_TYPE` ∈ {api, web, worker} and builds `./docker/${APP_TYPE}.Dockerfile`;
`CICD.yml` and `deploy.yml` matrix over the same three.

- [ ] **Step 4: Verify**

```bash
docker compose -f docker/docker-compose.yaml build
docker compose -f docker/docker-compose.yaml up -d
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5001/v1/health   # 200
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5000/login       # 200
docker restart hsm-app-api    # an authenticated Blazor session must survive
```

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "build: three Dockerfiles and compose services per host

Fixes a broken deploy — build.yml referenced docker/app.Dockerfile, which
never existed. Restarting hsm-app-api no longer disconnects Blazor circuits."
```

---

### Task 23: Telemetry destination from application configuration

**Files:**
- Create: `src/Hsm.Infrastructure/Telemetry/TelemetryRegistration.cs`
- Modify: all three `Program.cs`, all `appsettings*.json`, `docker/otel/otel-collector.yaml`
- Test: `tests/Hsm.Tests/Telemetry/TelemetryConfigurationTests.cs`

**Interfaces:**
- Produces: `static IHostApplicationBuilder AddHsmTelemetry(this IHostApplicationBuilder builder, string serviceName)`
  reading `Telemetry:Exporters` (`console` | `file` | `otlp`, comma-separated, per-signal overrides
  under `Telemetry:Traces|Metrics|Logs`).

- [ ] **Step 1: Write the failing configuration test**

Assert that a `console`-only configuration registers no OTLP exporter, and that `otlp` requires an
endpoint (a clear startup error rather than silent no-op).

- [ ] **Step 2: Implement `AddHsmTelemetry`**

One method all three hosts call, replacing their duplicated OTel blocks. Export failure must never
affect startup — the property U10 established still holds.

- [ ] **Step 3: Reduce the collector config to a passthrough**

`docker/otel/otel-collector.yaml` keeps OTLP receivers and the debug exporter only. Destination
selection now lives in app configuration, not this file.

- [ ] **Step 4: Verify by switching destinations**

Run `Hsm.Api` with `Telemetry:Exporters=console` (expect spans on stdout, nothing at the collector),
then `otlp` (expect spans at the collector), then `otlp` with the collector stopped (expect the host
to start and serve).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: telemetry destination is application configuration

The app decides where telemetry goes; the collector stops being the
switchboard. Console, local file, and OTLP are selectable per signal
without touching collector YAML."
```

---

### Task 24: Conventions document and architecture-doc amendments

**Files:**
- Create: `docs/reference/dotnet-conventions.md`
- Modify: `docs/ARCHITECTURE-DECISIONS.md`, `CLAUDE.md`, `AGENTS.md`

- [ ] **Step 1: Write the conventions document**

One page, no theory. A table mapping *kind of code* → *exact project and folder*: entity, command,
query, validator, module port, infrastructure-wide port, adapter, REST endpoint, Blazor page, UI
service, unit test, contract test, integration test, component test. Then the naming rule
(*libraries by layer, deployables by the door they open*), the dependency-arrow diagram, the
behavior order and why it is load-bearing, and a pointer to **`Hsm.Application/Users/`** as the
reference slice to copy.

- [ ] **Step 2: Amend the architecture decisions**

- §5.2: the client-isolation boundary is now **test-enforced, not compiler-enforced** (option B),
  with the mobile-extraction cost stated.
- §5.1: three deployables, not "one API process plus one worker".
- §6.4: Redis is no longer disposable — it holds queued jobs, so "no backup" must be revisited.
- §7.5: an API deploy no longer disconnects UI circuits.
- Decision log: add the dispatcher (and why not MediatR), CQRS slices, the three-host split, the
  durable queue correcting U14, and option B.

- [ ] **Step 3: Update `CLAUDE.md`**

New project list, the three `dotnet run` commands, the migrate command, the four test projects, and
the conventions-document pointer.

- [ ] **Step 4: Verify every referenced path exists, then commit**

```bash
git add -A && git commit -m "docs: conventions document and architecture amendments

States where each kind of code goes and names Users as the reference slice.
Records that the client-isolation boundary is now test-enforced, that Redis
holds durable state, and that the U14 in-process queue was a defect."
```

---

## Self-Review

**Spec coverage.** R1 → Tasks 2, 8–14. R2 → Tasks 5, 15. R3 → Tasks 4, 6, 7. R4 → Task 2 and the
global constraint. R5 → Task 1. R6/R8 → Task 17. R7 → Task 17 (UI dispatches in-process; no HTTP
client anywhere). R9 → Task 20. R10/R11 → Task 16. R12 → Task 19. R13 → Task 20. R14 → Task 21.
R15 → Task 22. R16 → Task 23. R17 → Tasks 8 and 24. No requirement is unclaimed.

**Placeholder scan.** One deliberate marker survives: the `NotImplementedException` in Task 8's
handler skeleton, which is explicitly labelled as "move this existing, already-tested body here"
rather than new logic. Tasks 9–14 give complete per-handler conversion tables instead of repeating
Task 8's code, which is total information for a mechanical repetition. Tasks 19–23 name the
mechanism for every previously vague step (Streams + consumer groups + delayed sorted set + dead
letter; `SET NX PX` leases; exact `dotnet ef` commands; base images and per-signal exporter keys).

**Type consistency.** `IRequest`/`ICommand`/`IQuery`/`Unit` (Task 2) are used unchanged in Tasks
5–20. `RequestActor`/`ICurrentPrincipal`/`AmbientPrincipal` (Task 3) are consumed by Tasks 5, 15, 19,
20. `RequireRoleAttribute`/`AllowAnonymousRequestAttribute`/`AllowPendingOnboardingAttribute`
(Task 5) are applied in Tasks 8–14. `ValidationFailure` (Task 6) is produced by Task 8's validator.
`IUnitOfWork` (Task 7) is implemented in Task 7 Step 4. `JobNameAttribute` (Task 19) is applied in
Tasks 12–13 — **note the forward reference**: those tasks add the attribute before Task 19 defines
it, so either land Task 19's attribute file early or apply the attributes in Task 19. Recorded here
rather than silently reordered, because Tasks 12–13 are otherwise independent of the queue.

**Known ordering risk.** Task 15 removes the edge gates only after Tasks 8–14 have annotated every
request type. If a module is reshaped without its policy attribute, Task 15 opens a hole in that
module — which is why Task 15 Step 5 requires the *entire* existing 401/403 corpus to pass, and why
Task 8's first test asserts the attribute rather than the behavior.

---

## Sources & References

- Prior plan: `docs/plans/2026-07-27-001-feat-dotnet-blazor-rewrite-plan.md`
- Release bar: `docs/plans/2026-07-27-002-minor-release-definition-of-done.md`
- Architecture decisions: `docs/ARCHITECTURE-DECISIONS.md` (§1 licensing, §5.1 monolith,
  §5.2 client isolation, §5.4 API contracts, §6 store roles, §7.5 Blazor operations)
- Frozen contract: `docs/reference/2026-07-27-frozen-api-contract.openapi.json`
- Frozen source: tag `freeze/typescript-2026-07-27`
