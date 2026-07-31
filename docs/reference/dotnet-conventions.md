# .NET conventions — where things go

One page. For "why", see `docs/ARCHITECTURE-DECISIONS.md`. For behavior, the frozen contract
(`docs/reference/2026-07-27-frozen-api-contract.openapi.json`) and its tests govern, not this
document.

## Where each kind of code goes

| Kind of code | Project | Folder | Example |
|---|---|---|---|
| Entity | `Hsm.Domain` | `{Module}/` | `Hsm.Domain/Identity/User.cs` |
| Command | `Hsm.Application` | `{Module}/Commands/{Verb}/` | `Users/Commands/CreateStaffUser/CreateStaffUserCommand.cs` |
| Query | `Hsm.Application` | `{Module}/Queries/{Verb}/` | `Users/Queries/GetUser/GetUserQuery.cs` |
| Validator | `Hsm.Application` | same folder as the command/query it validates | `IValidator<TRequest>` implementation next to the request record — see the Validator rule below before adding one |
| Module port (owned by one slice) | `Hsm.Application` | `{Module}/` (top level, not under Commands/Queries) | `Users/IStaffWelcomeEmailer.cs` |
| Infrastructure-wide port (a store role, §6 of the architecture doc) | `Hsm.Application` | `Ports/` | `Ports/IObjectStorage.cs` |
| Adapter (implements a port) | `Hsm.Infrastructure` | `{Module}/` or `{Role}/` matching the port's home | `Identity/BcryptPasswordHasher.cs`, `Persistence/EfUnitOfWork.cs`, `Jobs/RedisStreamJobConsumer.cs` |
| REST/FHIR endpoint | `Hsm.Api` | `{Module}/{Module}Endpoints.cs` | `Users/UserEndpoints.cs` |
| Blazor page (admin screen) | `Hsm.Web` | `Pages/Admin/` | `Pages/Admin/Users.razor` |
| UI service | interface in `Hsm.Contracts` (`Ui/`), implementation in `Hsm.Web` (`Services/`) | — | `Hsm.Contracts/Ui/IUsersAdminUiService.cs` + `Hsm.Web/Services/UsersAdminUiService.cs` |
| Unit test | `Hsm.Tests` | `{Module}/` (or `Architecture/` for boundary/purity tests) | `Hsm.Tests/Architecture/ScreenIsolationTests.cs` |
| Contract test | `Hsm.Contract.Tests` | `{Module}/` | `Hsm.Contract.Tests/Users/…` |
| Integration test | `Hsm.Integration.Tests` | flat, one file per concern, runs against real Postgres/Redis/RustFS containers | `Hsm.Integration.Tests/JobQueueDurabilityTests.cs` |
| Component test (bUnit, renders a `.razor` page) | `Hsm.Web.Tests` | flat, one file per page | `Hsm.Web.Tests/UsersPageTests.cs` |

**Reference slice to copy: `Hsm.Application/Users/`.** Commands and queries each in their own
folder, module port at the top level, handler alongside its command — copy this shape for every
new slice rather than reinventing folder layout per module.

## Naming rule

**Libraries are named by layer; deployables are named by the door they open.**

`Hsm.Domain`, `Hsm.Application`, `Hsm.Infrastructure`, `Hsm.Contracts` are libraries — their names
say which architectural layer they are, and each is referenced by more than one deployable.
`Hsm.Api`, `Hsm.Web`, `Hsm.Worker` are the three deployables — their names say which door onto the
same core each one opens (REST/FHIR, the staff Blazor shell, the background job consumer), not
which layer they're in, because each of them pulls in multiple layers.

## Dependency arrows

```
Hsm.Domain   (entities only, no project references)
Hsm.Contracts (leaf: UI service interfaces + wire-shared constants — no Hsm.* references, ever)

Hsm.Application ──> Hsm.Domain, Hsm.Contracts
Hsm.Infrastructure ──> Hsm.Application            (implements its ports)

Hsm.Api    ──> Hsm.Application, Hsm.Infrastructure, Hsm.Contracts
Hsm.Web    ──> Hsm.Application, Hsm.Infrastructure   (Hsm.Contracts transitively, for Ui/*)
Hsm.Worker ──> Hsm.Application, Hsm.Infrastructure
```

`Hsm.Contracts` staying a leaf **and** never referencing another `Hsm.*` assembly is what makes
the client-isolation boundary possible at all (`ContractsPurityTests`). Within `Hsm.Web`, the
`.razor` components themselves are held to a narrower rule than the project as a whole — see
"Client isolation is test-enforced" below.

## Behavior order — load-bearing

```
Telemetry → Authorization → Validation → Transaction
```

Registered outermost-first in `PipelineRegistration.AddHsmPipeline`. The order is not
cosmetic:

- **Telemetry is outermost** so it records every rejection, not just successes — a request
  refused by authorization or validation still gets a span.
- **Authorization runs before validation** so a caller who cannot perform the action is refused
  before the system spends any work parsing or checking its shape.
- **Transaction is innermost** so it is never opened around a request that was always going to be
  refused — no transaction begins, no connection is taken from the pool, unless the request has
  already cleared authorization and validation.

A command marked `[NoAmbientTransaction]` skips the transaction step entirely and owns its own
commits — see "NoAmbientTransaction is not a general escape hatch" below.

## Rules learned the hard way

- **Validator rule.** A guard moves into an `IValidator<TRequest>` only if the frozen envelope's
  refusal already has the `ValidationBehavior` shape (`issue.message` an array, `issue.errors`
  present). If the frozen response for that refusal is a plain 400 (`issue.message` a string), the
  guard stays as a throw inside the handler — do not force it into `IValidator` just because a
  validator "feels" like the right place. `CreateStaffUserHandler`'s patient-role guard is the
  worked example; the doc comment there explains the mismatch this rule prevents.

- **Exact-403-message lesson.** Before dropping or rewriting an edge-level auth check, grep the
  contract tests for an exact message or exception-type assertion first
  (`tests/Hsm.Contract.Tests/Shell/PipelineAuthorizationTests.cs`,
  `tests/Hsm.Contract.Tests/Shell/AdminScreensTests.cs`). Several of these pins were themselves
  authorized to change during this refactor (2026-07-31 user decision, see the architecture
  decision log) — but that was a deliberate call after inspection, not a side effect of an
  unrelated edit.

- **Slice results are domain entities, not DTOs.** `CreateStaffUserHandler` returns a `User`, not
  a `UserResponseDto`; the wire shape is produced at the endpoint by a projection function
  (`UserEndpoints.UserJson`), which is what keeps `PasswordHash` and other sensitive fields off
  the wire without a parallel DTO type per slice. This is deliberate deferred work, not an
  oversight: **new slices follow the same shape** — return the entity from the handler, shape it
  at the endpoint (or UI service) boundary. Do not add a DTO layer preemptively.

- **Never serialize a request payload into a span, log, or queue envelope blindly.**
  `TelemetryBehavior` records only the request's type name — nothing about its field values —
  because commands routinely carry passwords, tokens, and other secrets. This is a load-bearing
  invariant, not an oversight to "improve" later: any new telemetry or queue-envelope code that
  wants more than the type name must justify it field-by-field, not by dumping the object.

- **Ambient-transaction join rule.** `EfUnitOfWork` and `AuthUnitOfWork` **join** an already-open
  ambient transaction instead of nesting one (logged at Debug when this happens). A caller that
  needs an independent transaction — an audit row, an outbox write, a compensating action that
  must survive an outer rollback — must not go through either of these; it will be silently
  absorbed into the outer unit of work.

- **`[NoAmbientTransaction]` is not a general escape hatch.** It exists for exactly two commands,
  both queued jobs whose handlers persist a failure status and then re-throw so the queue's retry
  accounting sees the attempt: `DispatchEmailBatchCommand` and `RenderDocumentCommand`. Reach for
  it only when a handler has a failure path that must survive its own re-throw; both uses are
  pinned by contract tests (`ComsRequestPolicyTests`, `DocsRequestPolicyTests`) so the choice stays
  visible.

- **Queued job commands run through the same pipeline as everything else.** A job is dispatched
  through `IDispatcher`, not invoked directly — it is authorized against the actor that was
  current when it was *enqueued* (carried in the job envelope, reinstalled onto
  `AmbientPrincipal` by the consumer), not against some ambient worker identity. There is no
  separate, weaker authorization path for background work.

- **Developer role authorizes like production.** The developer-in-dev authorization bypass was
  removed (2026-07-31 user decision): a developer account in a dev environment is authorized by
  the same pipeline, the same role checks, as production. Do not reintroduce a dev-only bypass.

- **`onboardingCompletedAt` is rebuilt per request, not cached.** `RequestActorFactory` derives
  onboarding completion from the claim when present and non-empty, and otherwise falls back to the
  authoritative user row — on every request. Do not "optimize" this into a value stored once in a
  cookie claim; the fallback-to-row behavior is what makes a deleted or still-pending account fail
  closed, and a stale cached claim would defeat that.

- **Coms serialization is per-consuming-process, not cluster-wide.** `IsSerial`/single-consumer
  behavior on the `coms` queue only holds within one worker process; running more than one worker
  instance requires the lease (`ConsumerLease`) to hold cluster-wide serialization, with a 45-second
  failover window (`JobQueueTopology`'s `LeaseTtl`, deliberately larger than `Coms:ClaimMinIdleMs`
  so a crashed lease-holder is reclaimed before another instance double-claims its jobs).

- **`UsersAdminUiService.GetAssignableRolesAsync` has no request-level authorization.** It returns
  a compile-time constant (`RoleCatalog`) and discloses nothing about any account, so it is gated
  only by the Razor page's `[Authorize(Roles = admin)]`, not by a dispatched, pipeline-authorized
  request. This is a known, accepted exposure — not an oversight — documented here rather than
  given its own query slice.

- **Accepted contract drift from removing the edge gates (Task 15).** Two observable behavior
  changes vs. the frozen system are deliberate and permanent, not defects:
  - Every 403 across the whole API is now message-less (`ApiException.Forbidden()`), where the
    frozen system's message was `"Insufficient permissions"` on some routes. One authorizer, one
    shape.
  - On routes that used to be role- or onboarding-gated at the edge, authorization now runs
    *behind* body-shape validation (e.g. a non-clinical caller against a FHIR route now sees parse
    diagnostics before a 403, not instead of one). No PHI or write exposure results — the request
    is still refused — but the ordering of *which* refusal a caller sees changed.

- **Telemetry destination is `Telemetry:*` configuration, not collector config.**
  `Telemetry:Exporters` sets the default exporter list for every signal;
  `Telemetry:Traces|Metrics|Logs:Exporters` overrides per signal; `Telemetry:Otlp:Endpoint` is
  required if any signal selects `otlp`; `Telemetry:File:Directory` (default `telemetry`) sets
  where the file exporter writes when `file` is selected. See
  `Hsm.Infrastructure.Telemetry.AddHsmTelemetry`'s doc comment for the full scheme.

- **Dead-letter streams carry PHI with no TTL.** `RenderDocumentCommand.DataJson` and similar
  payloads ride the Redis dead-letter stream indefinitely on exhausted retries — there is no
  retention policy, no alerting, and no requeue tooling today. This is an open operational item,
  not a design decision to leave as-is; revisit before production data volume makes it a
  compliance problem. Relatedly, the `docs` queue's 10-minute `ClaimMinIdle` (vs. `coms`'s 30
  seconds) is a deliberate tradeoff against document-render duration, not a default — a mid-flight
  heartbeat (`XCLAIM`) to shorten dead-worker recovery is a deferred follow-up, not implemented.

## Client isolation is test-enforced, not compiler-enforced

Screens now live inside `Hsm.Web`, which references `Hsm.Application` and `Hsm.Infrastructure` for
its own DI wiring — so the compiler can no longer make a shortcut in a `.razor` file impossible
the way it could when screens lived in their own leaf project. Two tests in
`tests/Hsm.Tests/Architecture/ScreenIsolationTests.cs` are the replacement: one scans every
`.razor` file's `@using`/`@inject` directives for a forbidden namespace, the other reflects over
every compiled `IComponent` for an `[Inject]` property of a forbidden type. Keep both red/green —
if you need to bypass client isolation for a screen, that is a decision to make explicitly and
loudly (update the test), not something that can silently pass a build.
