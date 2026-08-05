# .NET conventions — where things go

One page. For "why", see `docs/ARCHITECTURE-DECISIONS.md`. For behavior, the committed OpenAPI
document (`docs/reference/openapi.json`) governs, not this document — regenerate it, never
hand-edit it (see the last rule under "Rules learned the hard way").

## Where each kind of code goes

| Kind of code | Project | Folder | Example |
|---|---|---|---|
| Entity | `Hsm.Domain` | `{Module}/` | `Hsm.Domain/Identity/HsmUser.cs` |
| Command | `Hsm.Application` | `{Module}/Commands/{Verb}/` | `Users/Commands/CreateStaffUser/CreateStaffUserCommand.cs` |
| Query | `Hsm.Application` | `{Module}/Queries/{Verb}/` | `Users/Queries/GetUser/GetUserQuery.cs` |
| Validator | `Hsm.Application` | same folder as the command/query it validates | `AbstractValidator<TRequest>` (FluentValidation) beside the request record — see the Validator rule below before adding one |
| Module port (owned by one slice) | `Hsm.Application` | `{Module}/` (top level, not under Commands/Queries) | `Users/IStaffWelcomeEmailer.cs` |
| Infrastructure-wide port (a store role, §6 of the architecture doc) | `Hsm.Application` | `Ports/` | `Ports/IObjectStorage.cs` |
| Adapter (implements a port) | `Hsm.Infrastructure` | `{Module}/` or `{Role}/` matching the port's home | `Identity/UserDirectory.cs`, `Persistence/EfUnitOfWork.cs`, `Queue/RedisStreamJobConsumer.cs` |
| REST/FHIR endpoint | `Hsm.Api` | `{Module}/{Module}Endpoints.cs` | `Users/UserEndpoints.cs` |
| Resource record | `Hsm.Api` | `{Module}/{Module}Resource.cs` | `Users/UserResource.cs` — see "allow-list" below |
| Blazor page (admin screen) | `Hsm.Web` | `Pages/Admin/` | `Pages/Admin/Users.razor` |
| UI service | interface in `Hsm.Contracts` (`Ui/`), implementation in `Hsm.Web` (`Services/`) | — | `Hsm.Contracts/Ui/IUsersAdminUiService.cs` + `Hsm.Web/Services/UsersAdminUiService.cs` |
| Unit test | `Hsm.Tests` | `{Module}/` (or `Architecture/` for boundary/purity tests) | `Hsm.Tests/Architecture/ScreenIsolationTests.cs` |
| API surface test | `Hsm.Api.Tests` | `{Module}/` | `Hsm.Api.Tests/Users/…` |
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
Hsm.Contracts (leaf: UI service interfaces + PagedResult<T> + wire-shared constants — no Hsm.* references, ever)

Hsm.Application ──> Hsm.Domain, Hsm.Contracts
Hsm.Infrastructure ──> Hsm.Application            (implements its ports)

Hsm.Api    ──> Hsm.Application, Hsm.Infrastructure, Hsm.Contracts
Hsm.Web    ──> Hsm.Application, Hsm.Infrastructure   (Hsm.Contracts transitively, for Ui/*)
Hsm.Worker ──> Hsm.Application, Hsm.Infrastructure
```

`Hsm.Contracts` staying a leaf **and** never referencing another `Hsm.*` assembly is what makes
the client-isolation boundary possible at all (`ContractsPurityTests`). Within `Hsm.Web`, the
`.razor` components themselves are held to a narrower rule than the project as a whole — see
"Client isolation is test-enforced" below. `PagedResult<T>` lives here rather than in
`Hsm.Application` because both `Hsm.Web`'s UI services and `Hsm.Api`'s endpoints need to name the
same paged shape, and `Hsm.Contracts` is the one project both can see without pulling in
`Hsm.Application`.

**`Hsm.Domain` has no project references, and exactly one framework package:
`Microsoft.Extensions.Identity.Stores`** (for `IdentityUser<TKey>`, which `HsmUser` extends). The
"entities only, no project references" rule is unchanged — nothing in `Hsm.Domain` points at
another assembly of ours — but a genuine domain entity has to derive from Identity's base class to
be the type `UserManager<HsmUser>` operates on directly. The alternative is a domain `User`, a
separate Identity-owned `HsmUser`, and a mapper that has to be right in both directions on every
write; the one framework package is cheaper than the second entity. See the doc comment on
`Hsm.Domain/Hsm.Domain.csproj` for the same reasoning in place. If this package reference is ever
removed, remove it *with* a mapped persistence user and a mapper, not by adding a second entity
that can drift from the first.

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

- **Validator rule.** There is no general 400, so there is no third option: a rule either checks
  the request's own shape, or it checks the resource's current state against the database. A
  request-shape or field rule (required, format, range, "these two fields are mutually exclusive")
  is an `AbstractValidator<TRequest>` in the pipeline. A rule that needs the database or the
  resource's current state — "this email is already taken", "onboarding is already complete",
  "this template is still in use" — is a `ConflictException` or `NotFoundException` thrown from the
  handler, because a validator that queries the database to decide pass/fail is doing the handler's
  job one layer too early and outside the transaction. If a guard doesn't fit either bucket, it is
  misdiagnosed, not a sign that a third mechanism is needed.

- **Slice results are domain entities, not DTOs.** `CreateStaffUserHandler` returns a `User`, not
  a `UserResponseDto`; the wire shape is produced at the endpoint by projecting into a
  `*Resource` record (e.g. `UserResource`), which is what keeps `PasswordHash` and other sensitive
  fields off the wire without a parallel DTO type per slice. The `*Resource` record is an
  **allow-list**: it names every field that goes on the wire, so a new column added to the entity
  later cannot leak by being forgotten — it has to be added to the resource record on purpose to
  ever reach a caller. **New slices follow the same shape** — return the entity from the handler,
  project it into a `*Resource` record at the endpoint (or UI service) boundary. Do not add a DTO
  layer beyond the resource record preemptively.

- **Never serialize a request payload into a span, log, or queue envelope blindly.**
  `TelemetryBehavior` records only the request's type name — nothing about its field values —
  because commands routinely carry passwords, tokens, and other secrets. This is a load-bearing
  invariant, not an oversight to "improve" later: any new telemetry or queue-envelope code that
  wants more than the type name must justify it field-by-field, not by dumping the object.

- **Ambient-transaction join rule.** `EfUnitOfWork` **joins** an already-open ambient transaction
  instead of nesting one (logged at Debug when this happens). A caller that needs an independent
  transaction — an audit row, an outbox write, a compensating action that must survive an outer
  rollback — must not go through it; it will be silently absorbed into the outer unit of work.

- **`[NoAmbientTransaction]` is not a general escape hatch.** It exists for exactly two commands,
  both queued jobs whose handlers persist a failure status and then re-throw so the queue's retry
  accounting sees the attempt: `DispatchEmailBatchCommand` and `RenderDocumentCommand`. Reach for
  it only when a handler has a failure path that must survive its own re-throw; both uses are
  pinned per-module by unit tests (`ComsRequestPolicyTests`, `DocsRequestPolicyTests`) in
  `tests/Hsm.Tests/` — they are not contract tests, nothing here touches the wire — and the
  carrier set as a whole (exactly these two, nothing else) is pinned across the entire
  `Hsm.Application` assembly by `RequestPolicyClosureTests`
  (`tests/Hsm.Tests/Architecture/RequestPolicyClosureTests.cs`), so a third command could not
  quietly pick up the attribute without a test failing somewhere.

- **Queued job commands run through the same pipeline as everything else.** A job is dispatched
  through `IDispatcher`, not invoked directly — it is authorized against the actor that was
  current when it was *enqueued* (carried in the job envelope, reinstalled onto
  `AmbientPrincipal` by the consumer), not against some ambient worker identity. There is no
  separate, weaker authorization path for background work.

- **Developer role authorizes like production.** The developer-in-dev authorization bypass was
  removed (2026-07-31 user decision): a developer account in a dev environment is authorized by
  the same pipeline, the same role checks, as production. Do not reintroduce a dev-only bypass.

- **`RefreshIntegrationTokensCommand` may only be constructed at the refresh edge.** It carries
  the raw opaque refresh token and nothing else, and is `[AllowAnonymousRequest]` — the token
  itself is the credential, not a session, so there is no principal for the pipeline to authorize
  the usual way; the handler learns which integration account it belongs to only by finding a
  stored digest that matches. Only `Hsm.Api.Identity.IdentityEndpoints` builds one; never dispatch
  it from a new call site.

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

- **Two antiforgery mechanisms, because they protect two different things.** The Blazor
  static-SSR login form (`Pages/Login.razor`) is antiforgery-protected via `EditForm`'s
  `FormName` plus `app.UseAntiforgery()` — that protects a browser form post. The REST door is
  protected by `Hsm.Api.Identity.HsmAntiforgery`, which validates the `X-XSRF-TOKEN` header on
  unsafe *cookie-authenticated* methods only — that protects a JSON request made with the session
  cookie. Both are standard `IAntiforgery`; a request authenticated with a bearer token instead of
  a cookie needs neither, because there is no ambient credential for a hostile page to ride.

- **Unknown JSON fields and unknown query parameters are ignored, not refused.**
  `forbidNonWhitelisted` was a NestJS behavior this project was reproducing during the freeze;
  System.Text.Json's default (ignore what it doesn't recognize) is the standard .NET behavior now,
  and nothing re-enables strict rejection. A client sending an extra field gets it silently
  dropped, not a 400.

- **Paging is `page`/`pageSize`, and an oversized `pageSize` is refused, not clamped.** Defaults
  are `page=1`, `pageSize=20`; the cap is 100. A caller who asks for `pageSize=5000` gets a
  validation error, not 100 rows delivered silently — see `PagingRules`
  (`Hsm.Application/Abstractions/PagingRules.cs`), which every paged query's validator calls so the
  numbers cannot drift between modules.

- **Both hosts must share one data-protection key ring and one `SetApplicationName`.** The session
  cookie is encrypted; a browser signed in at one door (`Hsm.Web`) must be recognized at the other
  (`Hsm.Api`), and vice versa. Left to defaults, each host generates its own ephemeral keys and the
  sibling sees an undecryptable blob — i.e. an anonymous request, which looks exactly like a bad
  password, not a configuration problem. `Hsm.Api.Tests`' shared `TestKeyRing`
  (`tests/Hsm.Api.Tests/Support/ApiFactory.cs`) proves the pattern for the in-memory test hosts; a
  real multi-instance deployment needs the same fix — a persisted, shared key ring and one
  application discriminator both hosts agree on.

- **`/health` is liveness-only and probes nothing.** `app.MapHealthChecks("/health")`
  (`Hsm.Api/Program.cs`) answers "is the process up", not "can it reach Postgres/Redis/RustFS".
  Dependency state is a separate, deliberately different endpoint: `GET /api/v1/system/status`
  (`Hsm.Api/System/SystemEndpoints.cs`), which dispatches a query and reports what it actually
  checked. Do not fold dependency checks into `/health` — a liveness probe that can fail because a
  downstream store is slow causes exactly the restart-loop it exists to prevent.

- **The committed OpenAPI document is generated, never hand-edited.**
  `docs/reference/openapi.json` is produced by
  `HSM_OPENAPI_UPDATE=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests` and reviewed as
  a diff like any other generated artifact. A hand edit will be silently overwritten the next time
  the generator runs, and will not have been reviewed as a real surface change in the meantime.

- **Telemetry destination is `Telemetry:*` configuration, not collector config.**
  `Telemetry:Exporters` sets the default exporter list for every signal;
  `Telemetry:Traces|Metrics|Logs:Exporters` overrides per signal; `Telemetry:Otlp:Endpoint` is
  required if any signal selects `otlp`; `Telemetry:File:Directory` (default `telemetry`) sets
  where the file exporter writes when `file` is selected. See
  `Hsm.Infrastructure.Telemetry.AddHsmTelemetry`'s doc comment for the full scheme.

- **Deploy order: migrate before rollout, always.** No service calls `Database.MigrateAsync()` on
  host boot — migrations are applied by an explicit step (`dotnet run --project src/Hsm.Api --
  --migrate`, see `Hsm.Api.MigrateCommand`), never implicitly. `.github/workflows/deploy.yml` is
  still a stub, but its step order already encodes the rule: migrate first, then roll out
  `api`/`web`/`worker`. Keep that order when the stub becomes real.

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
