---
title: "Clean Architecture + CQRS, three deployable hosts, durable worker"
type: refactor
status: active
date: 2026-07-28
origin: design conversation 2026-07-28 (no separate brainstorm document)
supersedes_decisions: "U14 in-process channel queue; single-host topology"
---

# Clean Architecture + CQRS, three deployable hosts, durable worker

## Summary

Reshape `Hsm.Application` into explicit command/query slices behind a dispatcher
with authorization, validation, transaction, and telemetry behaviors; split the
single host into three deployables (`Hsm.Api` for external integrations,
`Hsm.Web` for the Blazor UI, `Hsm.Worker` for durable background work); replace
the in-memory job queue with a Redis-backed one; and make the repository
deployable at all — it currently has no Dockerfile and no EF migrations.

---

## Problem Frame

The minor release shipped and works (305 tests green), but three things are
wrong with its shape, and one is a live defect.

**The defect.** U14 replaced the frozen system's Redis-backed BullMQ queue with
an in-memory `Channel<T>` inside the host process. Every queued email send and
document generation is lost on restart, and with more than one host instance
each instance holds its own invisible queue with nothing coordinating retries.
This is a regression from frozen behavior that was framed as a topology choice.

**The topology.** API and UI are fused in one process, so scaling external
integration capacity also scales the UI tier, and any API deploy disconnects
every staff member's Blazor circuit. `Hsm.Worker` exists but does nothing —
59 lines of heartbeat — because job processing was folded into the web host.

**The shape.** 51 handlers already implement one-use-case-per-class, which is
most of the CQRS pattern, but commands and queries are indistinguishable by
type or location, requests are loose parameter lists rather than objects, and
there is no pipeline. The consequence is not cosmetic: **authorization is
enforced separately at each edge** (`RequestAuth.GateAsync` in HTTP endpoints,
`UiServiceGate` in UI services), which the Phase 4 altitude review flagged as
"four modules, four answers, and a forgotten call fails open".

**Not deployable.** `.github/workflows/build.yml` builds `docker/app.Dockerfile`,
which does not exist. There are no EF migrations — tests use `EnsureCreated`, so
there is no way to create or evolve a real database.

---

## Requirements

**Application shape**
- R1. Commands and queries are distinct types, dispatched through one pipeline.
- R2. Authorization is a property of the command, enforced once, regardless of
  which host or transport dispatched it.
- R3. Validation, transaction scope, and per-use-case telemetry are pipeline
  behaviors, not repeated inside handlers.
- R4. No dependency on a commercially licensed mediator (MediatR v13+ fails the
  licensing constraint in `docs/ARCHITECTURE-DECISIONS.md` §1).
- R5. One statable rule for where ports live.

**Topology**
- R6. Three deployables: `Hsm.Api`, `Hsm.Web`, `Hsm.Worker`, independently
  scalable and independently deployable.
- R7. The Blazor UI calls handlers **in-process** — no internal HTTP API for our
  own UI (that would reintroduce the contract-negotiation tax that justified
  choosing Blazor Server).
- R8. `Hsm.Api` serves external integration consumers only.
- R9. Background work runs in `Hsm.Worker`, not in a request-serving host.

**UI boundary (option B)**
- R10. Screens live as folders inside `Hsm.Web`; the `Hsm.Web.Components`
  project is removed.
- R11. The client-isolation boundary is preserved by an **architecture test**
  rather than the compiler: no component may reach `Hsm.Application`,
  `Hsm.Infrastructure`, or EF Core. Components continue to consume
  contracts-declared UI service interfaces.

**Durability**
- R12. The job queue survives process restart and coordinates across instances.
- R13. Scheduled/cron work has a home.

**Deployability**
- R14. EF migrations exist, with a stated owner for running them.
- R15. Each host has a Dockerfile and a compose service named `hsm-app-*`.
- R16. Telemetry destination is chosen by **application configuration**, not by
  editing the collector's YAML.

**Ambiguity elimination**
- R17. A conventions document states where each kind of code goes, and one
  module is the worked reference implementation.

---

## Scope Boundaries

- No event sourcing, no separate read/write databases, no eventual consistency.
  "CQRS" here means command/query segregation with a pipeline, one database.
- No behavior changes to the frozen contract. The 258 contract tests are the
  guard: they exercise HTTP and know nothing about handler class names, so they
  must pass unmodified except for retargeting the host factory.
- No WASM/mobile host. Option B defers component extraction until that work is
  actually scheduled.
- No new REST routes. The internal admin queries stay in-process UI services.
- No Kubernetes. Compose plus the documented LXC topology.
- No sticky-session/Redis-backplane work for multiple `Hsm.Web` instances —
  configuration when uptime requirements harden, per the architecture doc.

---

## Key Technical Decisions

- **Hand-rolled dispatcher, ~60 lines.** MediatR v13+ is commercially licensed,
  which conflicts with a product that may ship to other hospitals. The pipeline
  is a decorator chain over `ICommandHandler<,>`/`IQueryHandler<,>`; no
  reflection-heavy framework needed.
- **Both `Hsm.Api` and `Hsm.Web` reference `Hsm.Application` directly.** They
  are two doors onto one core, not client and server. This keeps the UI's
  in-process handler calls (R7) while still giving independent scaling (R6).
- **A shared `Hsm.Hosting` library is the price of the split.** Cookie issuing,
  token-to-principal validation, the response envelope, and error middleware are
  needed by both HTTP hosts. Duplicating auth glue is how auth bugs happen, so
  it gets one home that both hosts reference. This is one project added; it is
  the honest cost of R6.
- **Redis backs the queue.** Already deployed, already the intended SignalR
  backplane. Job payloads are serialized commands, so a queued job takes the
  same validated/authorized/transactional path as an HTTP-dispatched one.
- **CQRS lands before the host split.** Reshaping first makes endpoints
  three-line dispatch calls, so moving them into `Hsm.Api` is a file move rather
  than a rewire. The reverse order does the endpoint work twice.
- **Migrations run as an explicit step, not on startup.** Two request-serving
  hosts racing `Migrate()` on boot is a known failure mode; a dedicated
  `--migrate` entry point (or init container) owns it.

---

## Target Structure

```
src/
  Hsm.Domain/            entities, invariants, domain services
  Hsm.Application/       ALL logic — command/query slices, ports, pipeline
  Hsm.Infrastructure/    EF Core, S3, Meilisearch, Redis, SMTP adapters
  Hsm.Contracts/         DTOs + UI service interfaces
  Hsm.Hosting/           shared HTTP concerns: envelope, auth cookies, errors

  Hsm.Api/          ▸ deployable — REST + FHIR for external integrations
  Hsm.Web/          ▸ deployable — Blazor Server host + the screens (option B)
  Hsm.Worker/       ▸ deployable — durable queue consumer + scheduled work

tests/
  Hsm.Tests/              unit + architecture — no infrastructure, fast CI job
  Hsm.Contract.Tests/     HTTP behavior vs the frozen contract
  Hsm.Integration.Tests/  adapters vs real Postgres/RustFS/Meilisearch
  Hsm.Web.Tests/          bUnit component tests
```

Dependency arrows, all inward:

```
Hsm.Api ────┐
Hsm.Web ────┼──► Hsm.Hosting ──► Hsm.Application ──► Hsm.Domain
Hsm.Worker ─┘                          ▲      │
                                       │      └──► Hsm.Contracts
                        Hsm.Infrastructure  (implements Application's ports)
```

Naming rule: **libraries are named by layer; deployables are named by the door
they open.**

Slice layout inside `Hsm.Application`:

```
Users/
  Commands/
    CreateStaffUser/{Command,Handler,Validator}.cs
    ChangeUserRole/{Command,Handler,Validator}.cs
  Queries/
    ListUsers/{Query,Handler}.cs
    GetUser/{Query,Handler}.cs
  IUserStore.cs                    ← module port lives with its module
Ports/
  IObjectStorage.cs  ISearchIndex.cs   ← ONLY infrastructure-wide ports
```

---

## Implementation Units

### Phase A — Application shape

- **UA1. Unify the ports split**

**Goal:** One statable rule for port location, ending the `Ports/` folder vs
per-module `Ports.cs` inconsistency.

**Requirements:** R5 · **Dependencies:** none

**Files:** move `src/Hsm.Application/{Auth,Users,Settings,Templates,Coms,Docs,Clinical}/Ports.cs`
contents into per-interface files beside their slice; keep only
`Ports/IObjectStorage.cs` and `Ports/ISearchIndex.cs`.

**Approach:** Pure file/namespace moves. No signature changes.

**Verification:** solution builds, 305 tests green, `Ports/` contains only
infrastructure-wide ports.

---

- **UA2. CQRS abstractions, dispatcher, and pipeline behaviors**

**Goal:** The dispatch mechanism and its cross-cutting behaviors, before any
handler is reshaped.

**Requirements:** R1, R3, R4 · **Dependencies:** UA1

**Files:** create `src/Hsm.Application/Abstractions/{ICommand,IQuery,ICommandHandler,IQueryHandler,IDispatcher,Dispatcher}.cs`
and `Abstractions/Behaviors/{Authorization,Validation,Transaction,Telemetry}Behavior.cs`.

**Approach:**
- `Dispatcher` resolves the handler for a request type from DI and wraps it in
  the registered behavior chain. No MediatR (R4).
- `TransactionBehavior` opens a transaction for `ICommand` only; queries never
  open one.
- `TelemetryBehavior` names its span after the request type, so every use case
  gets a span without per-handler code (OTel is already wired).
- `AuthorizationBehavior` reads a `[RequireRole]` attribute off the request type.

**Execution note:** Test-first. Write behavior tests that assert ordering
(authorization before validation before transaction) and that a query never
opens a transaction, and watch them fail before the chain exists.

**Test scenarios:** ordering; unauthorized command never reaches its handler;
validation failure short-circuits before the transaction opens; a handler
exception rolls the transaction back; span name matches the request type.

**Verification:** behavior tests pass; no `Hsm.Application` reference to any
mediator package.

---

- **UA3. Reshape the 51 handlers into slices**

**Goal:** Every use case is a command or a query with a request object, in its
own folder, dispatched through the pipeline.

**Requirements:** R1 · **Dependencies:** UA2

**Files:** all of `src/Hsm.Application/*/` reorganised into `Commands/` and
`Queries/` slice folders; every endpoint and UI service updated to dispatch.

**Approach:** Mechanical, one module at a time (Auth, Users, Settings,
Templates, Coms, Docs, Clinical), running the full suite after each. Handler
bodies move unchanged — only the entry signature becomes a request object.
Job handlers (`SendEmailJobHandler`, `GenerateDocumentJobHandler`) become
commands, which is what lets UC1 carry them over the queue.

**Execution note:** The 258 contract tests assert HTTP behavior and never name a
handler class, so they are the safety net — they must pass unmodified at every
step. If one fails, the reshape changed behavior and must be corrected, not the
test.

**Verification:** 305 tests green after each module; endpoints contain no
business logic; `dotnet format` clean.

---

- **UA4. Move authorization off the edges onto the commands**

**Goal:** Authorization enforced once by the pipeline, so it cannot be forgotten
by a new transport.

**Requirements:** R2 · **Dependencies:** UA3

**Files:** annotate commands/queries with `[RequireRole(...)]`; strip the
per-edge gates from `src/Hsm.Api/**` endpoints and the `UiServiceGate` calls in
`src/Hsm.Web/Services/**`.

**Approach:** The edge still authenticates (turns a cookie or bearer token into
a principal — that is transport work); it no longer authorizes. Every existing
403/401 contract test pins the observable result, so the move is verifiable.

**Test scenarios:** an admin-only command dispatched from a doctor session is
rejected identically via HTTP and via an in-process UI service; a command with
no attribute requires authentication but no role; anonymous dispatch is
rejected before the handler runs.

**Verification:** all existing authorization tests pass with the edge checks
removed; a new test proves a UI-service dispatch is rejected for a wrong-role
principal without any edge gate present.

---

### Phase B — Structure

- **UB1. Fold the screens into `Hsm.Web`, replace the compiler boundary with a test**

**Goal:** One web project (option B), with the client-isolation constraint
preserved by an architecture test.

**Requirements:** R10, R11 · **Dependencies:** UA4

**Files:**
- Move `src/Hsm.Web.Components/**` (17 files) into `src/Hsm.Web/{Pages,Layout}/`
  and friends; delete the project and its solution entry; move the MudBlazor
  package reference to `Hsm.Web`.
- Namespaces `Hsm.Web.Components.*` → `Hsm.Web.*`; drop
  `AddAdditionalAssemblies(...)` from `Program.cs` (same assembly now).
- Rewrite `tests/Hsm.Architecture.Tests/ClientIsolationBoundaryTests.cs` — the
  csproj-reference and transitive-closure assertions no longer apply.
- Rename `tests/Hsm.Web.Components.Tests` → `tests/Hsm.Web.Tests`.

**Approach:** The replacement test enforces the boundary two ways, because
neither alone is sufficient:
1. **Source scan** of every `.razor` file under the screen folders (including
   `_Imports.razor`, which could smuggle a namespace globally) for `@using` or
   `@inject` of `Hsm.Application`, `Hsm.Infrastructure`, or
   `Microsoft.EntityFrameworkCore`.
2. **Reflection** over every `ComponentBase`-derived type in `Hsm.Web`,
   asserting no injectable member's type comes from those assemblies.

**Execution note:** Observe the new test red before trusting it — add a
forbidden `@inject HsmDbContext` to a scratch component, confirm failure, remove
it. A boundary test that has never failed proves nothing. This is the same
discipline the compiler-enforced version got in U8.

**Test scenarios:** clean tree passes; a component injecting `HsmDbContext`
fails; a forbidden `@using` in `_Imports.razor` fails; a component injecting a
contracts-declared UI service passes.

**Verification:** 305 tests green; one web project; the boundary test observed
both green and red.

---

- **UB2. Extract `Hsm.Hosting`, split `Hsm.Api` out of `Hsm.Web`**

**Goal:** Two HTTP deployables sharing their transport plumbing, so external
integration traffic and the staff UI scale and deploy independently.

**Requirements:** R6, R7, R8 · **Dependencies:** UB1

**Files:**
- Create `src/Hsm.Hosting/`: response envelope, validation helpers, error
  middleware, `RequestAuth`, `AuthCookies`, CSRF protection.
- Create `src/Hsm.Api/`: `Program.cs` + the endpoint files moved out of
  `Hsm.Web` (`Auth`, `Users`, `Settings`, `Templates`, `Coms`, `Docs`, `Fhir`,
  `Health`).
- `src/Hsm.Web/` keeps the Blazor host, the screens, the UI services, and the
  Blazor auth scheme; loses all REST endpoints.
- Retarget the contract-test factory in `tests/Hsm.Contract.Tests/Support/` at
  `Hsm.Api`; the shell tests keep pointing at `Hsm.Web`.

**Approach:** Endpoints are dispatch three-liners after UA3, so this is a move
plus ~100 lines of DI wiring per host. Both hosts call
`AddHsmInfrastructure(...)`; neither gains business logic.

**Test scenarios:** every frozen-contract test passes against `Hsm.Api`; the
route-closure test runs against `Hsm.Api`'s endpoint set; the Blazor shell auth
tests pass against `Hsm.Web`; `Hsm.Web` exposes no `/v1` route.

**Verification:** both hosts boot and serve independently; killing `Hsm.Api`
leaves an authenticated Blazor session working.

---

- **UB3. Consolidate the test projects**

**Goal:** Four test projects, split by what they need to run.

**Dependencies:** UB2

**Files:** delete the empty `tests/Hsm.Domain.Tests` and
`tests/Hsm.Application.Tests`; create `tests/Hsm.Tests` holding the
architecture tests plus the pipeline-behavior unit tests from UA2; keep
`Hsm.Contract.Tests`, `Hsm.Integration.Tests`, `Hsm.Web.Tests`.

**Approach:** With `Hsm.Tests` being the only infrastructure-free project, the
CI unit job names projects explicitly and the `Infra` trait filter can retire.

**Verification:** `dotnet test tests/Hsm.Tests` needs no containers; CI unit job
green with no trait filter.

---

### Phase C — Durable worker

- **UC1. Redis-backed durable queue**

**Goal:** Queued work survives restarts and coordinates across instances.

**Requirements:** R12 · **Dependencies:** UA3

**Files:** replace `src/Hsm.Infrastructure/Jobs/ChannelJobQueue.cs` with a Redis
implementation behind the existing dispatcher ports; remove the in-process
channel processors from the HTTP hosts.

**Approach:** Payloads are serialized commands (UA3), so a dequeued job takes
the same pipeline as an HTTP dispatch. Preserve the frozen retry posture per
queue (coms 5 attempts / 5s exponential; docs 3 attempts / 1s initial / 2s
exponential), and keep coms strictly serial — the Phase 4 consolidation found
that resend ordering depends on it. At-least-once delivery with idempotent
handlers, matching the frozen webhook semantics.

**Test scenarios:** a job enqueued, then the consumer killed and restarted,
still runs (this is the defect being fixed); two consumers do not both process
one job; a job failing all attempts lands in a dead-letter list rather than
vanishing; duplicate delivery is idempotent.

**Verification:** integration tests against real Redis; no in-memory queue
remains.

---

- **UC2. `Hsm.Worker` becomes the real consumer, plus scheduling**

**Goal:** Background work runs in its own process, with a home for cron work.

**Requirements:** R9, R13 · **Dependencies:** UC1, UB2

**Files:** `src/Hsm.Worker/Program.cs` and the consumer host service; a
scheduler registering recurring commands.

**Approach:** The worker references `Hsm.Application` + `Hsm.Infrastructure`
like the other hosts and dispatches through the same pipeline. Scheduling starts
with a periodic-timer registry (a durable scheduler is only needed once a job
must fire exactly once across multiple workers — record it, don't build it).

**Test scenarios:** the worker processes a queued send end to end against real
Redis and Postgres; a scheduled command fires on its interval; restarting the
web host does not interrupt in-flight jobs.

**Verification:** background sends and document generation work with both HTTP
hosts stopped.

---

### Phase D — Deployability

- **UD1. Initial EF migration and a stated owner for running it**

**Goal:** A real database can be created and evolved. There are currently no
migrations at all.

**Requirements:** R14 · **Dependencies:** UB2

**Files:** `src/Hsm.Infrastructure/Migrations/**`; a `--migrate` entry point.

**Approach:** Generate the initial migration from the current model and verify
it produces the schema `EnsureCreated` produces today (that equivalence is the
whole test). Migrations are applied by an explicit step, never by a
request-serving host on boot — two hosts racing `Migrate()` is a known failure
mode.

**Test scenarios:** migrate an empty database, then run the integration suite
against it unchanged; migrating twice is a no-op; the migration-generated schema
matches the model (drift check).

**Verification:** integration tests pass against a migrated database rather than
`EnsureCreated`; a CI drift check fails when the model changes without a
migration.

---

- **UD2. Three Dockerfiles, compose services, CI publish**

**Goal:** The repository is deployable. `build.yml` currently builds a Dockerfile
that does not exist.

**Requirements:** R15 · **Dependencies:** UC2, UD1

**Files:** `docker/api.Dockerfile`, `docker/web.Dockerfile`,
`docker/worker.Dockerfile`; app services in `docker/docker-compose.yaml`
(`hsm-app-api`, `hsm-app-web`, `hsm-app-worker`); update
`.github/workflows/{build,CICD,deploy}.yml` for the three-host matrix.

**Approach:** Multi-stage `dotnet publish` builds. Compose app services stay out
of the devcontainer's `runServices` — the local loop keeps running hosts via
`dotnet run` for fast rebuilds; the compose services exist for
production-shaped runs and for the images CI publishes.

**Verification:** all three images build; `docker compose up` brings the full
stack healthy; a `docker restart hsm-app-api` leaves an authenticated Blazor
session connected.

---

### Phase E — Telemetry and conventions

- **UE1. Telemetry destination from application configuration**

**Goal:** The application decides where telemetry goes; the collector stops
being the switchboard.

**Requirements:** R16 · **Dependencies:** UB2

**Files:** telemetry registration in each host; `appsettings` schema.

**Approach:** A `Telemetry:Exporters` setting selects console, local file, and/or
OTLP endpoint, with per-signal (traces/metrics/logs) granularity. The collector
config drops to a plain OTLP passthrough. Export failure must still never affect
startup — the U10 property holds.

**Test scenarios:** console-only config emits no OTLP; file exporter writes to
the configured path; with OTLP configured and the collector stopped, the host
starts and serves.

**Verification:** switching destination requires no code change and no collector
edit.

---

- **UE2. Conventions document and reference slice**

**Goal:** Eliminate ambiguity about where code goes — the point of the whole
exercise.

**Requirements:** R17 · **Dependencies:** UA4, UB2

**Files:** `docs/reference/dotnet-conventions.md`; amend
`docs/ARCHITECTURE-DECISIONS.md` (§5.2 becomes test-enforced not
compiler-enforced; add the three-host topology, the dispatcher decision, and the
durable-queue correction); update `CLAUDE.md` commands and structure.

**Approach:** The document states, per kind of code, exactly which project and
folder it goes in, and names **Users** as the worked reference slice to copy
(two commands, two queries, a port, a validator, contract tests). One page, no
theory.

**Verification:** a reader can place a new use case without asking; every path
referenced in the document exists.

---

## System-Wide Impact

- **The frozen contract must not move.** 258 contract tests are the invariant
  across every unit here; they change only where they name the host factory.
- **Authorization moves layers** (UA4). Every 401/403 in the suite pins the
  observable behavior, so the move is verifiable — but this is the highest-risk
  unit in the plan and should not be batched with others.
- **`Hsm.Web.Components` disappearing** ends compiler enforcement of the client
  boundary. UB1's test is the replacement; if it is weak, the constraint is
  gone in practice. Mobile/WASM later requires extracting the screens back out,
  and this plan accepts that cost knowingly (option B).
- **Two hosts share one database.** Migrations get a single owner (UD1);
  connection-pool sizing is now per-host and should be set deliberately.
- **Redis becomes load-bearing** for job durability, not just cache. Its backup
  posture ("none — disposable") in the architecture doc needs revisiting, since
  a queued-but-unprocessed job is now real state.

---

## Risks & Dependencies

| Risk | Mitigation |
|---|---|
| The CQRS reshape silently changes behavior across 51 handlers | Contract tests never name handlers; run the full suite per module, and treat any failure as a real regression rather than a test to update |
| Authorization move (UA4) opens a hole | Every existing 401/403 test must pass with edge checks removed, plus a new test proving in-process dispatch is gated |
| The replacement boundary test (UB1) is weaker than the compiler | Two independent mechanisms (source scan + reflection), and the test must be observed red before it is trusted |
| Splitting hosts duplicates auth glue and drifts | `Hsm.Hosting` gives it one home; duplication is explicitly rejected |
| Redis queue introduces at-least-once semantics the frozen system handled differently | Frozen behavior was also at-least-once (BullMQ); idempotency tests already exist for webhooks and are extended to sends |
| Migration baseline diverges from the model | UD1 verifies the migrated schema equals the `EnsureCreated` schema, and a CI drift check keeps it that way |
| Plan is large enough to stall midway | Phases A, B, C, D, E each leave the tree green and deployable-or-better than before; stopping after any phase is safe |

---

## Sources & References

- Prior plan: `docs/plans/2026-07-27-001-feat-dotnet-blazor-rewrite-plan.md`
- Release bar: `docs/plans/2026-07-27-002-minor-release-definition-of-done.md`
- Architecture decisions: `docs/ARCHITECTURE-DECISIONS.md` (§1 licensing, §5.2
  client isolation, §5.4 API contracts, §6 store roles, §7.5 Blazor operations)
- Frozen contract: `docs/reference/2026-07-27-frozen-api-contract.openapi.json`
