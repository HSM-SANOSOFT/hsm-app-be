---
title: "Standard API surface: un-freeze the legacy contract"
type: refactor
status: active
date: 2026-08-03
origin: docs/brainstorms/2026-08-03-standard-api-surface-design.md (approved 2026-08-03)
supersedes_decisions: "frozen NestJS wire contract (envelope, exact messages, /v1 routes); hand-rolled IValidator; bcrypt + JWT-in-cookie identity stack; Infrastructure/Jobs naming; IAuthUnitOfWork"
---

# Standard API Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development`
> (recommended) or `superpowers:executing-plans` to implement this plan task-by-task.
> Steps use checkbox (`- [ ]`) syntax for tracking. `ce-work` can also execute it —
> the `## Tasks` sections below are its implementation units.

**Goal:** Replace the frozen NestJS wire contract with a standard ASP.NET Core surface:
`/api/v1` resource routes, plain resource JSON, RFC 9457 ProblemDetails, FluentValidation in
the existing pipeline, `PagedResult<T>`, full ASP.NET Core Identity (cookie + JWT bearer,
PBKDF2, Identity tables), a living OpenAPI spec behind Scalar, and the debt renames the
freeze forced us to defer.

**Architecture:** Unchanged. Clean Architecture with dependencies pointing inward
(`Hsm.Domain` ← `Hsm.Application` ← `Hsm.Infrastructure`) and three doors onto one core:
`Hsm.Api` (REST/FHIR), `Hsm.Web` (Blazor Server, in-process dispatch), `Hsm.Worker`
(durable queue consumer + scheduler). The CQRS dispatcher, the four pipeline behaviors,
every handler's business logic, the Redis Streams queue, the worker, the scheduler and the
Blazor screens all **survive this plan unchanged**. Only the wire surface and the identity
machinery change.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core + Npgsql, ASP.NET Core Identity,
FluentValidation, Blazor Server + MudBlazor 9.7, StackExchange.Redis (Streams), xUnit 2.9.3
+ bUnit, OpenTelemetry, `Microsoft.AspNetCore.OpenApi` + Scalar, QuestPDF, Firely SDK.

---

## Global Constraints

These are binding repository facts. Every task obeys all of them.

- **All dotnet commands run in the dev container:**
  `devcontainer exec --workspace-folder /home/rs/Documents/hsm/hsm-app bash -c 'cd /workspace && <cmd>'`
  The host has no .NET SDK. Every `Run:` line below is the `<cmd>` half of that invocation.
- **Warnings are errors.** `Directory.Build.props` sets `TreatWarningsAsErrors` and
  `AnalysisLevel=latest-recommended`. Analyzer violations fail the build — fix, never
  blanket-suppress.
- **`dotnet format Hsm.sln --verify-no-changes` must exit 0 before every commit.**
- **Central package management.** Versions live ONLY in `Directory.Packages.props`; project
  files carry `<PackageReference Include="..." />` with **no** `Version` attribute.
- **xunit is 2.9.3 (v2).** Use `CancellationToken.None` in test call sites. **Never**
  `TestContext.Current.CancellationToken` — that is xunit v3 API and does not compile here.
- **The frozen contract is RETIRED.** `tests/Hsm.Contract.Tests` (**246** tests as measured on
  this branch — the design doc's "261" predates the last consolidation) is *deleted* by
  Task 1, not kept green. The new gate is: **`dotnet test Hsm.sln` is green at the END of
  every task.** The tree always compiles and every remaining test passes at every commit.
- **FHIR endpoints keep their routes and their rendering.** `/fhir/R4/Patient` (search, read,
  create) stays at exactly those paths and keeps returning raw resources on success and
  `OperationOutcome` on error. Only the exception *types* it renders change.
- **Oracle: the app is PostgreSQL-only.** Nothing in this plan may reference, connect to, or
  issue any statement against the legacy Oracle database.
- **Dev DB reset is sanctioned.** The `Initial` migration is regenerated from scratch in
  Task 11 and every dev/test database is dropped once. `EnsureCreated` is gone and does not
  come back.
- **Pipeline authorization remains the sole authorizer.** `[RequireRole]` /
  `[AllowAnonymousRequest]` / `[AllowPendingOnboarding]` on request types, enforced by
  `AuthorizationBehavior`. Do **not** introduce endpoint-level `[Authorize]` or
  `.RequireAuthorization(...)` role checks on `/api` routes. Authentication middleware yes —
  authorization stays in the pipeline.

---

## Requirements

Distilled from `docs/brainstorms/2026-08-03-standard-api-surface-design.md`. Every one is
claimed by a task in the Self-Review at the end.

**Wire surface**
- R1. Every non-FHIR route lives under `/api/v1/...`; FHIR stays at `/fhir/R4`.
- R2. Success responses are plain resource JSON — no `{metadata, data}` envelope. Status
  codes carry the meaning: 200 read/update, 201 + `Location` create, 202 accepted-async,
  204 no content.
- R3. Resources are plural nouns; no verbs in paths except explicit actions
  (`/resend`, `/validate`, `/draft-render`); GET never has side effects; JSON is camelCase.
- R4. Paged collections return `PagedResult<T>` = `{items, page, pageSize, totalItems,
  totalPages}` with `page`/`pageSize` query parameters.

**Errors**
- R5. All failures render RFC 9457 ProblemDetails (`application/problem+json`) from **one**
  `IExceptionHandler`, always carrying `type`, `title`, `status` and `traceId`.
- R6. `ApiException` is deleted and replaced by a closed exception set in
  `Hsm.Application/Errors/`: `UnauthorizedException` (401), `ForbiddenException` (403),
  `NotFoundException(resource, id)` (404), `ConflictException` (409),
  `TooManyRequestsException` (429), FluentValidation's `ValidationException` (400), anything
  else 500 with no caller-facing detail.
- R7. The FHIR surface renders the same exception set as `OperationOutcome`.
- R8. The rate limiter's rejection path emits a 429 ProblemDetails, not a bespoke body.

**Validation**
- R9. Validation has one home: FluentValidation `AbstractValidator<TRequest>` executed by the
  existing pipeline `ValidationBehavior`, so HTTP, Blazor and queued dispatch validate
  identically. The hand-rolled `IValidator`/`ValidationFailure`/`BodyValidator` are deleted.
- R10. Validation failures render `ValidationProblemDetails` with camelCase field keys.

**Identity**
- R11. Membership is ASP.NET Core Identity: `HsmUser : IdentityUser<Guid>`,
  `HsmDbContext : IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>`, PBKDF2 hashing,
  `UserManager`/`SignInManager`. bcrypt and the SHA-256 pre-digest are deleted.
- R12. Every role in `RoleCatalog` is seeded as an Identity role.
- R13. Browsers authenticate with the Identity application cookie (encrypted, HttpOnly,
  SameSite=Strict, sliding); integrations authenticate with `AddJwtBearer`.
- R14. CSRF is standard ASP.NET antiforgery (readable token + header validated on unsafe
  cookie-authenticated methods); `GET /api/v1/identity/csrf` issues the token.
- R15. Password reset and username recovery use Identity token providers, keeping
  enumeration-safe responses and the existing recovery rate limits.
- R16. Integration refresh tokens are opaque high-entropy values, SHA-256-hashed at rest,
  rotated on use; `POST /api/v1/identity/refresh` serves integrations only.
- R17. The `Initial` migration is regenerated from scratch and the model has no pending
  changes against it.

**Debt**
- R18. `Infrastructure/Jobs` → `Infrastructure/Queue` (namespace and `Jobs:*` config keys).
- R19. `Application/Auth` and `Api/Auth` → `Identity`.
- R20. `IAuthUnitOfWork` is retired into `IUnitOfWork`.
- R21. Every "frozen X" doc comment is deleted from `src/`.

**Docs and tests**
- R22. `AddOpenApi()` + Scalar UI at `/api`, spec at `/api/openapi.json`, config-gated by
  `OpenApi:Enabled` (on in Development, off by default elsewhere).
- R23. The generated spec is committed at `docs/reference/openapi.json` and a test fails when
  the generated document and the committed one diverge.
- R24. `tests/Hsm.Api.Tests` pins the new surface: per-route status codes, ProblemDetails
  shapes, the 401/403 auth matrix, pagination, and antiforgery behavior.

---

## Scope Boundaries

- No Angular client adaptation. Clients adapt to the new API after this branch merges.
- No SMS. `POST /coms/send/sms` is dropped, not reimplemented.
- No 2FA, no external logins, no Identity Razor UI pages.
- No dead-letter operations tooling; `deploy.yml` stays a stub.
- No changes to the dispatcher, the behavior order, the queue implementation, the worker, the
  scheduler, or the Blazor screens' markup.
- **`GET /api/v1/templates` stays unpaged.** It returns a plain JSON array. The template
  catalog is an admin-managed bounded set filtered by `category`, it never had `page`/`limit`
  parameters, and adding them is a store change with no consumer asking for it. This is the
  one deliberate exception to R4, recorded so nobody "fixes" it by accident.
- No new source project. `tests/Hsm.Api.Tests` is the only new project in the whole plan.
- No DTO layer per slice: handlers keep returning domain entities and the endpoint projects
  them into a resource record (the conventions doc's existing rule, unchanged).

---

## Key Technical Decisions

- **One `IExceptionHandler`, not per-route try/catch.** `AddProblemDetails()` +
  `app.UseExceptionHandler()` + a single `HsmExceptionHandler` replaces
  `ApiErrorHandling.UseApiErrorEnvelope`, `ApiErrorGuard` and `ErrorStatusCodes`. `traceId`
  is added once by `CustomizeProblemDetails`, so no mapping branch can forget it.

- **The exception set is closed and has no generic 400.** Every current
  `ApiException.BadRequest(...)` call site is reclassified individually in Task 2's sweep
  table — a field-shaped refusal becomes a `ValidationException`, a state-conflict refusal
  becomes a `ConflictException`, and a server-misconfiguration refusal becomes a plain
  `InvalidOperationException` (500). "Add a `BadRequestException`" was rejected because it is
  exactly the catch-all `ApiException` this plan exists to delete.

- **FHIR maps `ValidationException` to 422, everything else to its own status.** The FHIR
  carve-out is the one place a validation failure is not a 400: FHIR clients expect 422 for a
  malformed resource, and `FhirResponses` is already a separate renderer. Five exception
  types, two renderers, one status divergence — documented rather than smuggled.

- **Three Identity packages across three layers, deliberately.**
  `Microsoft.Extensions.Identity.Stores` in `Hsm.Domain` (it defines `IdentityUser<TKey>`),
  `Microsoft.Extensions.Identity.Core` in `Hsm.Application` (handlers use
  `UserManager<HsmUser>`), `Microsoft.AspNetCore.Identity.EntityFrameworkCore` in
  `Hsm.Infrastructure` (the EF stores), and `Microsoft.AspNetCore.Identity` reaches only
  `Hsm.Api`/`Hsm.Web` (`SignInManager` needs `IHttpContextAccessor` and the authentication
  scheme provider, which are transport concerns). The alternative — a domain `User` plus a
  separate persistence-layer identity user plus a mapper — is two user types and a mapping
  layer, which is the duplication the rewrite removed.

- **`HsmUser` absorbs the profile columns; our `IUserStore` shrinks to `IUserDirectory`.**
  `UserManager` covers find-by-name, find-by-email, create, password change and role
  assignment. What it does not cover — the scalar onboarding probe and the paged admin
  listing — stays behind a port. It is **renamed** to `IUserDirectory` because
  `Microsoft.AspNetCore.Identity.IUserStore<TUser>` is a real type that will be in scope in
  the same files, and two `IUserStore`s is a compile error waiting to happen.

- **One adaptive authentication scheme, two handlers.** A policy scheme forwards to
  `JwtBearer` when the request carries `Authorization: Bearer `, and to the Identity
  application cookie otherwise. This reproduces the current `RequestAuth`
  cookie-then-bearer resolution order with framework middleware instead of hand-rolled
  token reading.

- **The actor is installed by middleware, not by each endpoint.** `RequestAuth.GateAsync`
  disappears. After `UseAuthentication()`, one `UseHsmActor()` middleware builds the
  `RequestActor` from `HttpContext.User` (roles from role claims, onboarding from a claim
  with the authoritative row as fallback) and stores it in `HttpContext.Items`.
  `HttpCurrentPrincipal` reads it back exactly as it does today. An endpoint cannot forget to
  authenticate any more, because it never had to remember.

- **Role rows are seeded by the migration, not by host boot.** `IdentityRole<Guid>` rows are
  emitted with `HasData` using ids derived deterministically from the role name
  (`SHA-256(name)[0..16]`), so the seed lives in the regenerated `Initial` migration and two
  hosts racing a seeder is not a failure mode that exists.

- **The OpenAPI gate compares a generated document to a committed file.** The document
  transformer clears `servers` so the artifact is host-independent; the test writes the file
  and fails loudly when `HSM_OPENAPI_UPDATE=1` is set, so regenerating is one command and
  never a silent overwrite.

- **The auth seam.** `tests/Hsm.Api.Tests` gets one helper —
  `AuthenticatedClientAsync(role, onboarded)` — written in Task 1 over the *current* cookie
  JWT machinery. Every module task (5–10) authenticates through it. Task 12 rewrites its body
  for the Identity cookie and Task 13 retargets its login route. Its **signature never
  changes**, so the identity swap rewrites one method instead of every module's auth matrix.

---

## File Structure

Target layout after Task 17. `→` marks a move, `✚` a new file, `✖` a deletion.

```
src/
  Hsm.Domain/
    Identity/
      HsmUser.cs                       ✚ IdentityUser<Guid> + profile columns
      RoleCatalog.cs                     + IdFor(role) deterministic role ids
      IntegrationAccount.cs              unchanged
      IntegrationRefreshToken.cs       → split out of RefreshTokens.cs
      User.cs  UserRole.cs  PasswordResetToken.cs  RefreshTokens.cs   ✖
  Hsm.Application/
    Errors/
      HsmExceptions.cs                 ✚ the closed set
      ApiException.cs                  ✖
    Abstractions/
      PagingRules.cs                   ✚ shared FluentValidation page rules
      IValidator.cs                    ✖ (FluentValidation replaces it)
      Behaviors/ValidationBehavior.cs    rewritten over IValidator<T> (FluentValidation)
      IUnitOfWork.cs                     + SaveChangesAsync
    Identity/                          → from Application/Auth
      IUserDirectory.cs                → renamed from Auth/IUserStore.cs, shrunk
      IIntegrationAccountStore.cs  IIntegrationRefreshTokenStore.cs
      IRecoveryEmailer.cs  IEnvironmentPolicy.cs  RequestActorFactory.cs
      IntegrationTokenIssuer.cs        → from Auth/TokenIssuer.cs, integrations only
      IAuthTokenCodec.cs  IPasswordHasher.cs  IAuthUnitOfWork.cs
      IPasswordResetTokenStore.cs  IUserRefreshTokenStore.cs  TokenDigests.cs   ✖
      Commands/{Register,Login,Logout,CompleteOnboarding,ForgotPassword,
                ResetPassword,RecoverUsername,RegisterIntegration,
                LogoutIntegration,IssueIntegrationTokens,
                RefreshIntegrationTokens,RevokeIntegrationTokens}/
      Commands/{GeneratePin,ValidatePin,RefreshTokens,Signup,SignupIntegration}/  ✖
      Queries/ListIntegrationAccounts/
    Users/  Settings/  Templates/  Coms/  Docs/  Clinical/  System/
      … slices unchanged in shape; validators become AbstractValidator<T>
  Hsm.Infrastructure/
    Identity/
      IdentityRegistration.cs          ✚ AddHsmIdentity + cookie/JWT config
      HsmUserClaimsPrincipalFactory.cs ✚ emits the onboarding claim
      UserDirectory.cs                 ✚ replaces IdentityStores.UserStore
      IdentityStores.cs                  shrunk: integration account + refresh tokens
      BcryptPasswordHasher.cs  JwtAuthTokenCodec.cs → IntegrationTokenCodec.cs
    Queue/                             → from Infrastructure/Jobs (git mv)
      RedisStreamJobQueue.cs  RedisStreamJobConsumer.cs  JobQueueTopology.cs
      JobQueueRegistration.cs  JobNameRegistry.cs  JobEnvelope.cs
      DelayedJobPump.cs  JobConnection.cs
    Persistence/HsmDbContext.cs          now IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>
    Migrations/                          REGENERATED — single Initial migration
  Hsm.Contracts/
    PagedResult.cs                     ✚
    Auth/AuthCookiePolicy.cs           ✖ (cookie config is Identity options now)
    Ui/*.cs                              unchanged
  Hsm.Api/
    Program.cs                           rewritten pipeline
    Errors/HsmExceptionHandler.cs      ✚
    OpenApi/OpenApiRegistration.cs     ✚
    Identity/                          → from Api/Auth
      IdentityEndpoints.cs             → from AuthEndpoints.cs
      HsmActorMiddleware.cs            ✚ replaces RequestAuth.GateAsync
      HttpCurrentPrincipal.cs
      HsmAntiforgery.cs                ✚ replaces CsrfProtection.cs
      AuthCookies.cs  CsrfProtection.cs  RequestAuth.cs   ✖
    Http/{ApiEnvelope,ApiErrorHandling,ErrorStatusCodes,Validation}.cs   ✖
    Users/{UserEndpoints,UserResource}.cs
    Emails/{EmailEndpoints,EmailResource}.cs      → from Coms/ComsEndpoints.cs
    Webhooks/WebhookEndpoints.cs                  → from Coms/ComsEndpoints.cs
    Documents/{DocumentEndpoints,DocumentResource}.cs  → from Docs/DocsEndpoints.cs
    Templates/{TemplateEndpoints,TemplateResource}.cs
    Settings/{SettingsEndpoints,SettingsResource}.cs
    System/SystemEndpoints.cs                     → from Health/HealthEndpoints.cs
    Fhir/{FhirEndpoints,FhirResponses,PatientFhirMapper}.cs   routes unchanged
  Hsm.Web/
    Auth/HsmCookieAuthenticationHandler.cs  ✖ (Identity cookie replaces it)
    Auth/{ShellActor,HsmAuthenticationStateProvider,AuthPrincipalClaims}.cs  reworked
tests/
  Hsm.Api.Tests/                       ✚ replaces Hsm.Contract.Tests
    Support/{ApiFactory,ShellFactory,SurfaceRouter,ProblemAssert}.cs
    {Identity,Users,Emails,Documents,Templates,Settings,System,Fhir,OpenApi}/
    Shell/{PipelineAuthorizationTests,AdminScreensTests,ShellAuthenticationTests}.cs
  Hsm.Contract.Tests/                  ✖
  Hsm.Tests/  Hsm.Integration.Tests/  Hsm.Web.Tests/    kept
docs/reference/
  openapi.json                         ✚ the living spec
  2026-07-27-frozen-api-contract.openapi.json   ✖
  2026-07-27-frozen-api-routes.txt              ✖
```

---

## Ordering note — read before starting

**Tasks 2–10 run on the OLD auth machinery.** The identity swap does not land until Tasks
11–14. During Tasks 2–10 the edge still authenticates with `RequestAuth.GateAsync` reading
the JWT access cookie, and login is still `POST /v1/auth/login` (Task 13 is what moves it).

That is deliberate: reshaping ten modules and swapping the entire identity stack in the same
task would leave no green tree in between. The cost is that the module tasks' auth-matrix
tests authenticate against machinery that is about to be replaced — which is why **Task 1
writes the seam**: one `AuthenticatedClientAsync(role, onboarded)` helper on the test
factory. Module tests call only that. Task 12 rewrites its body (cookie mechanism), Task 13
rewrites its route (`/v1/auth/login` → `/api/v1/identity/login`), and no module test file is
touched by either.

The second ordering consequence: `/v1/auth/*` routes stay alive and enveloped until Task 13
deletes them. Task 2 therefore has to leave `ApiEnvelope` standing for the auth module while
it deletes the envelope everywhere else. Task 2's Step 8 states exactly which envelope call
sites survive into Task 13 and why.

---

## Tasks

### Task 1: Retire the frozen contract; stand up `Hsm.Api.Tests`

The frozen contract stops being the specification here. 261 contract tests and the frozen
OpenAPI snapshot are deleted in one commit, and a new test project takes their place with the
factory patterns that were worth keeping — dedicated database per suite, migrate-not-create,
per-suite Redis namespace, the two-host shell topology — plus the auth seam every later task
depends on.

**Files:**
- Delete: `tests/Hsm.Contract.Tests/` (entire project, 11 folders, 246 tests),
  `docs/reference/2026-07-27-frozen-api-contract.openapi.json`,
  `docs/reference/2026-07-27-frozen-api-routes.txt`
- Create: `tests/Hsm.Api.Tests/Hsm.Api.Tests.csproj`,
  `tests/Hsm.Api.Tests/Support/ApiFactory.cs`,
  `tests/Hsm.Api.Tests/Support/ShellFactory.cs`,
  `tests/Hsm.Api.Tests/Support/ProblemAssert.cs`,
  `tests/Hsm.Api.Tests/System/HealthSmokeTests.cs`
- Move: `tests/Hsm.Contract.Tests/Shell/{PipelineAuthorizationTests,AdminScreensTests,ShellAuthenticationTests}.cs`
  → `tests/Hsm.Api.Tests/Shell/`
- Modify: `Hsm.sln`, `.github/workflows/pr-validation.yml`

**Interfaces:**
- Consumes: `Hsm.Api.Program`, `Hsm.Web.Program`, `Hsm.Infrastructure.Persistence.HsmDbContext`,
  `Hsm.Application.Auth.IPasswordHasher`, `Hsm.Domain.Identity.{User,UserRole,RoleCatalog}`.
- Produces — every later task's tests consume these exact signatures:
  - `abstract class ApiHostFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>`
    with `protected abstract string DatabaseName { get; }`,
    `protected virtual string JobKeyPrefix { get; }`,
    `protected virtual bool ConsumesJobs { get; }`,
    `protected virtual void ConfigureModule(IWebHostBuilder builder)`,
    `protected virtual Task OnSchemaCreatedAsync()`
  - `Task EnsureSchemaAsync()`
  - `HttpClient CreateApiClient()`
  - `Task<Guid> SeedUserAsync(string username, string password, string role, DateTimeOffset? onboardingCompletedAt, string? email = null, bool isActive = true)`
  - **`Task<HttpClient> AuthenticatedClientAsync(string role, bool onboarded = true, string? username = null)` — THE AUTH SEAM.**
  - `abstract class ApiFactory : ApiHostFactory<Hsm.Api.Program>`
  - `abstract class ShellFactory : ApiHostFactory<Hsm.Web.Program>` (Hsm.Api sidecar + `SurfaceRouter`)
  - `static class ProblemAssert` — `Task<JsonElement> ProblemAsync(HttpResponseMessage, int expectedStatus)`

**Decision recorded here: the shell suites move into `Hsm.Api.Tests/Shell/`, not into
`Hsm.Web.Tests`.** `Hsm.Web.Tests` is the infrastructure-free bUnit project and CI's
`unit-tests` job runs it with no Postgres, no Redis and no RustFS. The three shell suites boot
two live hosts against a real database; putting them there would break that job's defining
property. They keep the two-host factory (Blazor host + `Hsm.Api` sidecar behind a
path-routing handler), which is the deployment in miniature and is the only thing that proves
one sign-in serves both doors.

- [ ] **Step 1: Delete the frozen contract and its tests**

```bash
dotnet sln Hsm.sln remove tests/Hsm.Contract.Tests/Hsm.Contract.Tests.csproj
git rm -r --quiet tests/Hsm.Contract.Tests
git rm --quiet docs/reference/2026-07-27-frozen-api-contract.openapi.json
git rm --quiet docs/reference/2026-07-27-frozen-api-routes.txt
```

Before deleting, keep a copy of the three shell suites for Step 4:

```bash
mkdir -p /tmp/shell-suites
git show HEAD:tests/Hsm.Contract.Tests/Shell/PipelineAuthorizationTests.cs > /tmp/shell-suites/PipelineAuthorizationTests.cs
git show HEAD:tests/Hsm.Contract.Tests/Shell/AdminScreensTests.cs        > /tmp/shell-suites/AdminScreensTests.cs
git show HEAD:tests/Hsm.Contract.Tests/Shell/ShellAuthenticationTests.cs > /tmp/shell-suites/ShellAuthenticationTests.cs
```

`tests/Hsm.Contract.Tests/Routes/RouteClosureTests.cs` is **not** carried over: it diffed the
implemented route set against the frozen snapshot, and the snapshot no longer exists. Task 16's
`generated == committed` OpenAPI gate is its replacement.

- [ ] **Step 2: Create the test project**

```bash
dotnet new xunit -o tests/Hsm.Api.Tests -n Hsm.Api.Tests
dotnet sln Hsm.sln add tests/Hsm.Api.Tests/Hsm.Api.Tests.csproj
rm tests/Hsm.Api.Tests/UnitTest1.cs
```

Then overwrite `tests/Hsm.Api.Tests/Hsm.Api.Tests.csproj` with exactly this — no
`TargetFramework`, no `Nullable`, no `ImplicitUsings`, no `Version` attributes:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Hsm.Api\Hsm.Api.csproj" />
    <ProjectReference Include="..\..\src\Hsm.Web\Hsm.Web.csproj" />
    <ProjectReference Include="..\..\src\Hsm.Worker\Hsm.Worker.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write the host factory, carrying only what survives**

`tests/Hsm.Api.Tests/Support/ApiFactory.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Api.Auth;
using Hsm.Application.Abstractions;
using Hsm.Application.Auth;
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Hsm.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Api.Tests;

/// <summary>
/// Boots a real host against the dev container's PostgreSQL on a database
/// DEDICATED to the concrete factory, so sibling suites can never collide.
/// Carried over from the retired contract suite: per-suite database,
/// migrate-not-create, per-suite queue namespace, module hooks. Dropped:
/// the frozen JWT/CSRF secret pins that no longer describe anything.
/// </summary>
public abstract class ApiHostFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>Password every seeded account shares. Tests never assert on it.</summary>
    public const string SeedPassword = "Seed-Password-1!";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, bool> ReadySchemas = new(StringComparer.Ordinal);

    private readonly string _jobKeyPrefix = $"hsmtest:{Guid.NewGuid():N}";

    /// <summary>The dedicated database this factory's host runs against.</summary>
    protected abstract string DatabaseName { get; }

    /// <summary>The same value, reachable by a sibling host on the same data.</summary>
    internal string Database => DatabaseName;

    /// <summary>This factory's queue namespace (Jobs:KeyPrefix; Queue:KeyPrefix after Task 15).</summary>
    protected virtual string JobKeyPrefix => _jobKeyPrefix;

    internal string JobNamespace => JobKeyPrefix;

    /// <summary>Whether this host also PROCESSES what it enqueues (coms/docs suites).</summary>
    protected virtual bool ConsumesJobs => false;

    protected sealed override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:HsmDb", ConnectionStringFor(DatabaseName));
        builder.UseSetting("Jobs:KeyPrefix", JobKeyPrefix);
        builder.UseSetting("OpenApi:Enabled", "true");
        if (ConsumesJobs)
        {
            builder.ConfigureServices(services =>
            {
                services.AddHsmJobProcessing();

                // A job scope has no HttpContext — the consumer installs the
                // envelope's actor on AmbientPrincipal instead. An HTTP request
                // ALWAYS reads HttpCurrentPrincipal, so an ambient actor can
                // never leak into a route.
                services.AddScoped<HttpCurrentPrincipal>();
                services.AddScoped<ICurrentPrincipal>(sp =>
                    sp.GetRequiredService<IHttpContextAccessor>().HttpContext is null
                        ? sp.GetRequiredService<AmbientPrincipal>()
                        : sp.GetRequiredService<HttpCurrentPrincipal>());
            });
        }

        // HS256 keys must be at least 256 bits. Task 14 reduces these to the
        // integration-token secret alone.
        builder.UseSetting("Auth:JwtAccessSecret", "api_test_at_secret_0123456789abcdef");
        builder.UseSetting("Auth:JwtRefreshSecret", "api_test_rt_secret_0123456789abcdef");
        builder.UseSetting("Auth:CsrfSecret", "api_test_csrf_secret_0123456789abcdef");
        builder.UseSetting("Auth:Environment", "dev");
        ConfigureModule(builder);
    }

    /// <summary>Module-specific settings and service replacements.</summary>
    protected virtual void ConfigureModule(IWebHostBuilder builder)
    {
    }

    internal void ApplyModuleConfiguration(IWebHostBuilder builder) => ConfigureModule(builder);

    private static string ConnectionStringFor(string database)
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__HsmDb")
            ?? "Host=postgres;Port=5432;Username=postgres;Password=postgres;Database=hsm";
        var withoutDatabase = configured
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', withoutDatabase.Append($"Database={database}"));
    }

    /// <summary>Drops and MIGRATES the dedicated database once per test run.</summary>
    public async Task EnsureSchemaAsync()
    {
        if (ReadySchemas.ContainsKey(DatabaseName))
        {
            return;
        }

        await SchemaGate.WaitAsync();
        try
        {
            if (!ReadySchemas.ContainsKey(DatabaseName))
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
                await db.Database.EnsureDeletedAsync();
                await db.Database.MigrateAsync();
                await OnSchemaCreatedAsync();
                ReadySchemas[DatabaseName] = true;
            }
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    /// <summary>Extra one-time provisioning after the schema exists (e.g. buckets).</summary>
    protected virtual Task OnSchemaCreatedAsync() => Task.CompletedTask;

    /// <summary>A client that neither follows redirects nor manages cookies —
    /// cookie behavior is under test.</summary>
    public virtual HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    /// <summary>Seeds a user directly and returns its id.</summary>
    public async Task<Guid> SeedUserAsync(
        string username,
        string password,
        string role,
        DateTimeOffset? onboardingCompletedAt,
        string? email = null,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email ?? $"{username}@api.test",
            PasswordHash = hasher.Hash(password),
            FirstName = "Api",
            FirstLastName = "Test",
            OnboardingCompletedAt = onboardingCompletedAt,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        user.Roles.Add(new UserRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Role = role,
            Domain = RoleCatalog.DomainOf(role) ?? "System",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// THE AUTH SEAM. Seeds an account in <paramref name="role"/> and returns a
    /// client already carrying its session.
    ///
    /// <para>Tasks 2-10 run on the pre-Identity machinery, so the body below
    /// signs in through POST /v1/auth/login and replays the Set-Cookie header.
    /// Task 12 replaces the mechanism (Identity application cookie) and Task 13
    /// replaces the route (/api/v1/identity/login). The SIGNATURE does not
    /// change in either task, which is the whole point: no module suite is
    /// rewritten when identity lands.</para>
    /// </summary>
    public async Task<HttpClient> AuthenticatedClientAsync(
        string role, bool onboarded = true, string? username = null)
    {
        var name = username ?? $"u{Guid.NewGuid():N}"[..20];
        await SeedUserAsync(name, SeedPassword, role, onboarded ? DateTimeOffset.UtcNow : null);

        var client = CreateApiClient();
        var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { username = name, password = SeedPassword },
            CancellationToken.None);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Seed sign-in for role '{role}' failed with {(int)response.StatusCode}.");
        }

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => value.Split(';', 2)[0])
            : [];
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<HsmDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        return await work(db);
    }
}

/// <summary>The REST door. Every API suite boots Hsm.Api.</summary>
public abstract class ApiFactory : ApiHostFactory<Hsm.Api.Program>
{
}
```

`tests/Hsm.Api.Tests/Support/ShellFactory.cs` — carried over verbatim from the retired
`ContractShellFactory`, renamed, with the `SurfaceRouter` prefix list updated from
`/v1` to `/api` (both are needed until Task 13 retires the last `/v1` route, so the router
matches all three prefixes for the duration of this plan):

```csharp
using Microsoft.AspNetCore.Hosting;

namespace Hsm.Api.Tests;

/// <summary>
/// The staff door. Shell suites boot Hsm.Web — Blazor, screens, UI services,
/// no REST — and reach the API surface through a SIDECAR Hsm.Api host on the
/// same database, behind one client that routes by path. That is the
/// deployment in miniature: sign in through either door and the other
/// recognizes you.
/// </summary>
public abstract class ShellFactory : ApiHostFactory<Hsm.Web.Program>
{
    private static readonly string[] ApiPrefixes = ["/api", "/fhir", "/v1"];

    private readonly ApiSurfaceFactory _apiSurface;

    protected ShellFactory() => _apiSurface = new ApiSurfaceFactory(this);

    public override HttpClient CreateApiClient() =>
        new(new SurfaceRouter(_apiSurface.Server.CreateHandler(), Server.CreateHandler(), ApiPrefixes))
        {
            BaseAddress = new Uri("http://localhost"),
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _apiSurface.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The REST door, on the shell factory's database and settings.</summary>
    private sealed class ApiSurfaceFactory(ShellFactory shell) : ApiHostFactory<Hsm.Api.Program>
    {
        protected override string DatabaseName => shell.Database;

        protected override string JobKeyPrefix => shell.JobNamespace;

        protected override void ConfigureModule(IWebHostBuilder builder) =>
            shell.ApplyModuleConfiguration(builder);
    }

    /// <summary>Path-prefix routing across the two in-memory hosts.</summary>
    private sealed class SurfaceRouter : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _api;
        private readonly HttpMessageInvoker _shell;
        private readonly string[] _apiPrefixes;

        public SurfaceRouter(HttpMessageHandler api, HttpMessageHandler shell, string[] apiPrefixes)
        {
            _api = new HttpMessageInvoker(api);
            _shell = new HttpMessageInvoker(shell);
            _apiPrefixes = apiPrefixes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "/";
            var target = _apiPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal))
                ? _api
                : _shell;
            return target.SendAsync(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _api.Dispose();
                _shell.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 4: Re-home the three shell suites**

Copy the files saved in Step 1 into `tests/Hsm.Api.Tests/Shell/`, change the namespace from
`Hsm.Contract.Tests.Shell` to `Hsm.Api.Tests.Shell`, and change each factory base class from
`ContractShellFactory` to `ShellFactory`. Their assertions are untouched in this task —
Tasks 2 and 12 update the ones that name `ApiException` or the CSRF error string, and each of
those tasks says so explicitly.

- [ ] **Step 5: Write the ProblemDetails assertion helper (used from Task 2 onward)**

`tests/Hsm.Api.Tests/Support/ProblemAssert.cs`:

```csharp
using System.Text.Json;

namespace Hsm.Api.Tests;

/// <summary>
/// Every error assertion in this project goes through here, so the invariant
/// "problem+json, with status and traceId, on every failure" is stated once.
/// </summary>
public static class ProblemAssert
{
    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, int expectedStatus)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        var problem = JsonDocument.Parse(body).RootElement;
        Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.True(problem.TryGetProperty("traceId", out var traceId));
        Assert.False(string.IsNullOrWhiteSpace(traceId.GetString()));
        return problem;
    }
}
```

- [ ] **Step 6: Write the smoke test**

`tests/Hsm.Api.Tests/System/HealthSmokeTests.cs`. It targets `/v1/health`, the route that
exists today; **Task 10 Step 6 retargets it to `/health`**.

```csharp
namespace Hsm.Api.Tests.System;

public sealed class HealthSmokeFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_system";
}

public class HealthSmokeTests(HealthSmokeFactory factory) : IClassFixture<HealthSmokeFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Host_boots_and_serves_health()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/health", CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 7: Point CI at the new project**

In `.github/workflows/pr-validation.yml`, in the `integration-tests` job, replace:

```yaml
      - name: Run infra-backed contract tests (auth, dedicated database)
        run: |
          dotnet test tests/Hsm.Contract.Tests --configuration Release
```

with:

```yaml
      - name: Run API surface tests (dedicated database per suite)
        run: |
          dotnet test tests/Hsm.Api.Tests --configuration Release
```

**Do not touch the `pr-gate` job's name or its `needs:` list.** All three rulesets in
`.github/rulesets/` reference `pr-gate` by name; renaming it silently un-gates `development`,
`main` and `release/**`.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet build Hsm.sln && dotnet test Hsm.sln`
Expected: PASS. The suite is 246 tests smaller, 17 shell tests came back, and one smoke test
is new. Nothing else changed.

- [ ] **Step 9: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "test: retire the frozen contract, stand up Hsm.Api.Tests

The frozen NestJS wire contract is no longer the specification: 246 contract
tests, the OpenAPI snapshot and the route list are deleted. Hsm.Api.Tests
takes over with the parts worth keeping — a dedicated database per suite,
migrate-not-create, a per-suite queue namespace, and the two-host shell
topology — plus AuthenticatedClientAsync, the seam Task 12 later retargets
so no module suite is rewritten when Identity lands."
```

---

### Task 2: The closed exception set and ProblemDetails

`ApiException` is a status-code catch-all: any handler can raise any status with any message,
and the renderer reproduces a NestJS envelope nobody is entitled to any more. It is replaced
by five named exceptions with fixed statuses and one `IExceptionHandler`.

**Files:**
- Create: `src/Hsm.Application/Errors/HsmExceptions.cs`,
  `src/Hsm.Api/Errors/HsmExceptionHandler.cs`
- Delete: `src/Hsm.Application/Errors/ApiException.cs`,
  `src/Hsm.Api/Http/ErrorStatusCodes.cs`, `src/Hsm.Api/Http/ApiErrorHandling.cs`
- Modify: `src/Hsm.Api/Program.cs`, `src/Hsm.Api/Fhir/FhirResponses.cs`,
  `src/Hsm.Api/Fhir/FhirEndpoints.cs`, `src/Hsm.Api/Fhir/PatientFhirMapper.cs`,
  `src/Hsm.Api/Http/ApiEnvelope.cs` (shrunk, deleted in Task 13),
  `src/Hsm.Api/Http/Validation.cs` (its `ApiValidationException` retargeted, deleted in Task 3),
  `src/Hsm.Api/Auth/RequestAuth.cs`, every handler in the sweep table below,
  `src/Hsm.Web/Services/{SignInUiService,DocumentsAdminUiService}.cs`,
  `src/Hsm.Application/Abstractions/Behaviors/{AuthorizationBehavior,ValidationBehavior}.cs`,
  `src/Hsm.Application/{Templates/TemplateErrors.cs,Coms/ComsErrors.cs}`
- Test: `tests/Hsm.Api.Tests/Errors/ProblemDetailsTests.cs`,
  `tests/Hsm.Tests/Abstractions/AuthorizationBehaviorTests.cs` (retargeted)

**Interfaces:**
- Consumes: `IPipelineBehavior<,>`, `ICurrentPrincipal` (unchanged).
- Produces — Tasks 3–17 throw and catch exactly these:
  - `abstract class HsmException : Exception` — the root every mapped failure derives from
  - `sealed class UnauthorizedException(string? message = null)` → 401
  - `sealed class ForbiddenException(string? message = null)` → 403
  - `sealed class NotFoundException(string resource, object id)` → 404, with
    `string Resource { get; }` and `string Id { get; }`
  - `sealed class ConflictException(string message)` → 409
  - `sealed class TooManyRequestsException(string? message = null)` → 429
  - `sealed class HsmExceptionHandler : IExceptionHandler`

- [ ] **Step 1: Write the failing ProblemDetails tests**

`tests/Hsm.Api.Tests/Errors/ProblemDetailsTests.cs`:

```csharp
using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Errors;

public sealed class ProblemDetailsFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_errors";
}

public class ProblemDetailsTests(ProblemDetailsFactory factory)
    : IClassFixture<ProblemDetailsFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Unauthenticated_request_is_a_401_problem()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/v1/user", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task Wrong_role_is_a_403_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.GetAsync("/v1/user", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task Missing_resource_is_a_404_problem_naming_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            $"/v1/user/{Guid.NewGuid()}", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 404);
        Assert.Contains("User", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_username_is_a_409_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"dup{Guid.NewGuid():N}"[..20];
        var body = new
        {
            username,
            email = $"{username}@api.test",
            firstName = "A",
            firstLastName = "B",
            role = Roles.Nurse,
            tempPassword = "Temp-Passw0rd",
        };
        var first = await client.PostAsJsonAsync("/v1/user/staff", body, CancellationToken.None);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync(CancellationToken.None));

        var second = await client.PostAsJsonAsync("/v1/user/staff", body, CancellationToken.None);

        await ProblemAssert.ProblemAsync(second, 409);
    }

    [Fact]
    public async Task Unexpected_failure_is_a_500_problem_with_no_internal_detail()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        // A non-GUID id reaches Guid.Parse and throws FormatException, which is
        // outside the closed set and must surface as a bare 500.
        var response = await client.GetAsync("/v1/user/not-a-guid", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 500);
        Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind is System.Text.Json.JsonValueKind.String
            && detail.GetString()!.Contains("FormatException", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Api.Tests --filter ProblemDetailsTests`
Expected: FAIL — every case returns the `{metadata, issue}` envelope with content type
`application/json`, so `ProblemAssert` fails on the media type first.

- [ ] **Step 3: Write the closed exception set**

`src/Hsm.Application/Errors/HsmExceptions.cs`:

```csharp
namespace Hsm.Application.Errors;

/// <summary>
/// The root of every failure the application deliberately raises. One handler in
/// each transport maps this closed set onto a status code; anything NOT derived
/// from it is a bug and surfaces as a bare 500 with no caller-facing detail.
///
/// <para>There is deliberately no general-purpose "BadRequestException". A 400
/// is what a malformed request gets, and malformed requests are the validators'
/// business (FluentValidation, Task 3). A refusal that is not about the request's
/// shape is a Conflict, a Forbidden, or a bug — reintroducing a catch-all here
/// would rebuild the ApiException this type replaced.</para>
/// </summary>
public abstract class HsmException(string? message) : Exception(message);

/// <summary>No credential, or a credential that no longer authenticates. 401.</summary>
public sealed class UnauthorizedException(string? message = null) : HsmException(message);

/// <summary>Authenticated, but not permitted. 403.</summary>
public sealed class ForbiddenException(string? message = null) : HsmException(message);

/// <summary>
/// The addressed resource does not exist. 404. Carries the resource NAME and the
/// identifier separately so a caller can tell "no such document" from "no such
/// template" without parsing prose, and so the message is built one way everywhere.
/// </summary>
public sealed class NotFoundException(string resource, object id)
    : HsmException($"{resource} '{id}' was not found.")
{
    public string Resource { get; } = resource;

    public string Id { get; } = id?.ToString() ?? string.Empty;
}

/// <summary>
/// The request is well-formed but conflicts with the resource's current state:
/// a duplicate key, an already-completed onboarding, a template still in use. 409.
/// </summary>
public sealed class ConflictException(string message) : HsmException(message);

/// <summary>A per-account or per-caller quota is exhausted. 429.</summary>
public sealed class TooManyRequestsException(string? message = null) : HsmException(message);
```

- [ ] **Step 4: Write the exception handler**

`src/Hsm.Api/Errors/HsmExceptionHandler.cs`:

```csharp
using FluentValidation;
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hsm.Api.Errors;

/// <summary>
/// The ONE place an exception becomes a response on this door. Every failure
/// renders RFC 9457 problem+json; `traceId` is attached centrally by
/// AddProblemDetails' CustomizeProblemDetails, so no branch here can forget it.
///
/// <para>Only <see cref="HsmException"/> and FluentValidation's
/// <see cref="ValidationException"/> carry caller-facing text. Everything else
/// is a bug: it is logged with its full detail and answered with a bare 500,
/// because an exception message from an unplanned path is exactly the kind of
/// thing that leaks a connection string or a row's contents.</para>
/// </summary>
public sealed partial class HsmExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<HsmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = Map(exception);
        var status = problem.Status ?? StatusCodes.Status500InternalServerError;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, httpContext.Request.Method, httpContext.Request.Path, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    /// <summary>The closed set, and nothing else, decides a status code.</summary>
    internal static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => ValidationProblem(validation),
        UnauthorizedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => Problem(StatusCodes.Status403Forbidden, "Forbidden"),
        NotFoundException notFound =>
            Problem(StatusCodes.Status404NotFound, "Not Found", notFound.Message),
        ConflictException conflict =>
            Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message),
        TooManyRequestsException => Problem(StatusCodes.Status429TooManyRequests, "Too Many Requests"),
        _ => Problem(StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };

    /// <summary>
    /// ValidationProblemDetails with camelCase keys, because the wire is
    /// camelCase everywhere else and a client should not have to know that
    /// validators name C# properties.
    /// </summary>
    internal static ProblemDetails ValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => CamelCasePath(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
        };
    }

    /// <summary>`Files[0].FileName` becomes `files[0].fileName`.</summary>
    internal static string CamelCasePath(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : string.Join('.', propertyName.Split('.').Select(CamelCaseSegment));

    private static string CamelCaseSegment(string segment) =>
        segment.Length == 0 || char.IsLower(segment[0])
            ? segment
            : char.ToLowerInvariant(segment[0]) + segment[1..];

    /// <summary>
    /// Title and Type are left to ProblemDetailsDefaults where we do not set
    /// them; it fills the RFC 9110 status URI and the canonical reason phrase.
    /// </summary>
    private static ProblemDetails Problem(int status, string title, string? detail = null) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
    };

    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled failure serving {Method} {Path}.")]
    private static partial void LogUnhandled(
        ILogger logger, string method, string path, Exception exception);
}
```

- [ ] **Step 5: Wire it into `Program.cs`**

Register, replacing the `UseApiErrorEnvelope()` line and the rate limiter's `OnRejected` body:

```csharp
// RFC 9457 for every failure on this door. traceId is attached HERE, once, so
// a mapping branch cannot ship without it — and the Activity id is preferred
// over TraceIdentifier because that is what a reader will find in the traces.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<HsmExceptionHandler>();
```

```csharp
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        // The rejection never reaches an endpoint, so nothing throws — the 429
        // problem is written here directly, in the same shape the handler
        // produces for TooManyRequestsException.
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context.HttpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                },
            });
    };
```

And in the middleware pipeline, `app.UseApiErrorEnvelope();` becomes:

```csharp
app.UseExceptionHandler();
```

placed **first**, before `UseHsts`/`UseHttpsRedirection`/`UseRateLimiter`, so everything
downstream — including the antiforgery middleware Task 12 adds — renders through it.

- [ ] **Step 6: Sweep every `ApiException` call site**

This is the complete inventory. Nothing here is left to judgement at implementation time.

**Straight renames — same status, new type:**

| File | Was | Becomes |
|---|---|---|
| `Application/Abstractions/Behaviors/AuthorizationBehavior.cs:33` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Abstractions/Behaviors/AuthorizationBehavior.cs:37` | `ApiException.Forbidden()` | `new ForbiddenException()` |
| `Application/Abstractions/Behaviors/AuthorizationBehavior.cs:42` | `ApiException.Forbidden()` | `new ForbiddenException("Onboarding is not complete.")` |
| `Application/Auth/TokenIssuer.cs:27` | `ApiException.Forbidden(msg)` | `new ForbiddenException(msg)` |
| `Application/Auth/Commands/Login/LoginHandler.cs:16,20` | `ApiException.Unauthorized(msg, InvalidCredentials)` | `new UnauthorizedException("Invalid username or password.")` — **same message on both branches**, keeping the path enumeration-safe |
| `Application/Auth/Commands/Logout/LogoutHandler.cs:18,23` | `ApiException.Unauthorized(msg)` | `new UnauthorizedException(msg)` |
| `Application/Auth/Commands/LogoutIntegration/LogoutIntegrationHandler.cs:18,22` | `ApiException.Unauthorized(msg)` | `new UnauthorizedException(msg)` |
| `Application/Auth/Commands/RefreshTokens/RefreshTokensHandler.cs:25,33` | `ApiException.Unauthorized(msg)` | `new UnauthorizedException(msg)` |
| `Application/Auth/Commands/CompleteOnboarding/CompleteOnboardingHandler.cs:20` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Auth/Commands/CompleteOnboarding/CompleteOnboardingHandler.cs:24` | `ApiException.NotFound(msg)` | `new NotFoundException("User", actor.Id)` |
| `Application/Auth/Commands/ForgotPassword/ForgotPasswordHandler.cs:29` | `ApiException.TooManyRequests()` | `new TooManyRequestsException()` |
| `Application/Auth/Commands/IssueIntegrationTokens/IssueIntegrationTokensHandler.cs:15` | `ApiException.NotFound(msg)` | `new NotFoundException("IntegrationAccount", request.AccountId)` |
| `Application/Users/Queries/GetUser/GetUserHandler.cs:15` | `ApiException.NotFound(msg)` | `new NotFoundException("User", request.UserId)` |
| `Application/Users/Commands/ChangeUserRole/ChangeUserRoleHandler.cs:23` | `ApiException.NotFound(msg)` | `new NotFoundException("User", request.UserId)` |
| `Application/Users/Commands/ChangeOwnPassword/ChangeOwnPasswordHandler.cs:23` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Users/Commands/ChangeOwnPassword/ChangeOwnPasswordHandler.cs:27` | `ApiException.NotFound(msg)` | `new NotFoundException("User", actor.Id)` |
| `Application/Users/Commands/UpdateOwnProfile/UpdateOwnProfileHandler.cs:20,24` | `Unauthorized()` / `NotFound(msg)` | `new UnauthorizedException()` / `new NotFoundException("User", actor.Id)` |
| `Application/Settings/Commands/UpdateSettings/UpdateSettingsHandler.cs:23` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Docs/Queries/GetDocument/GetDocumentHandler.cs:13,17` | `Unauthorized()` / `NotFound(msg)` | `new UnauthorizedException()` / `new NotFoundException("Document", request.Id)` |
| `Application/Docs/Queries/GetDocumentUrl/GetDocumentUrlHandler.cs:13,17,22` | `Unauthorized()` / `NotFound(msg)` ×2 | `new UnauthorizedException()` / `new NotFoundException("Document", request.Id)` / `new NotFoundException("DocumentFile", request.Id)` |
| `Application/Docs/Commands/{GenerateDocument:26,UploadDocuments:13,DeleteDocument:13}` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Docs/Commands/DeleteDocument/DeleteDocumentHandler.cs:17` | `ApiException.NotFound(msg)` | `new NotFoundException("Document", request.Id)` |
| `Application/Coms/Commands/SendEmail/SendEmailHandler.cs:34` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Application/Coms/Commands/ReceiveWebhook/ReceiveWebhookHandler.cs:52` | `new ApiException(401, …)` | `new UnauthorizedException("Webhook signature verification failed.")` |
| `Application/Coms/ComsErrors.cs:16,19` | `ApiException.NotFound(msg)` | `new NotFoundException("EmailBatch", id)` / `new NotFoundException("EmailRecipient", id)` — the helpers keep their names and change their return type to `NotFoundException` |
| `Application/Templates/TemplateErrors.cs:9` | `ApiException.NotFound(msg)` | `new NotFoundException("Template", identifier)` |
| `Application/Templates/TemplateErrors.cs:11-18` | `new ApiException(409, …)` ×2 | `new ConflictException(msg)` ×2 (name taken, template in use) |
| `Application/Clinical/Queries/GetPatient/GetPatientHandler.cs:20` | `new ApiException(404, …)` | `new NotFoundException("Patient", request.Id)` |
| `Application/Clinical/Commands/CreatePatient/CreatePatientHandler.cs:20` | `new ApiException(409, …)` | `new ConflictException(msg)` |
| `Web/Services/DocumentsAdminUiService.cs:32` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |
| `Web/Services/SignInUiService.cs:38` | `catch (ApiException)` | `catch (HsmException)` |
| `Api/Auth/RequestAuth.cs:61,79,80` | `ApiException.Unauthorized(...)` | `new UnauthorizedException(...)` — `TOKEN_EXPIRED`/`INVALID_TOKEN` labels are dropped; the whole file dies in Task 12 |
| `Api/Auth/AuthEndpoints.cs:94` | `ApiException.Unauthorized()` | `new UnauthorizedException()` |

**Reclassifications — the status changes, deliberately:**

| File | Was | Becomes | Why |
|---|---|---|---|
| `Auth/Commands/Logout/LogoutHandler.cs:28` | 400 "no active session" | `ConflictException("No active session to end.")` (409) | State conflict, not a malformed request |
| `Auth/Commands/LogoutIntegration/LogoutIntegrationHandler.cs:28` | 400 "already logged out" | `ConflictException(...)` (409) | Same |
| `Auth/Commands/CompleteOnboarding/CompleteOnboardingHandler.cs:28` | 400 email mismatch | `ValidationException([new ValidationFailure("confirmEmail", "Confirmation email does not match the account email.")])` (400) | It IS a field failure; it just needs the row to check against, so it stays in the handler and throws the validator's exception type |
| `Auth/Commands/CompleteOnboarding/CompleteOnboardingHandler.cs:33` | 400 already onboarded | `ConflictException("Onboarding is already complete.")` (409) | State conflict |
| `Auth/Commands/ResetPassword/ResetPasswordHandler.cs:23,33` | 400 generic | `ValidationException([new ValidationFailure("token", "The password reset link is invalid or has expired.")])` (400) | **Both branches keep the identical message** — that is what makes the route enumeration-safe |
| `Auth/Commands/IssueIntegrationTokens/IssueIntegrationTokensHandler.cs:19` | 400 inactive account | `ConflictException("Integration account is not active.")` (409) | State conflict |
| `Users/Commands/ChangeUserRole/ChangeUserRoleHandler.cs:19` | 400 unknown role | **deleted** — the rule moves to `ChangeUserRoleValidator` in Task 3 | Request-shape rule; a validator is its home |
| `Users/Commands/CreateStaffUser/CreateStaffUserHandler.cs:27` | 400 patient-facing role | **deleted** — moves to `CreateStaffUserValidator` in Task 3 | Same |
| `Users/Commands/ChangeOwnPassword/ChangeOwnPasswordHandler.cs:31` | 401 wrong current password | `ValidationException([new ValidationFailure("currentPassword", "Current password is incorrect.")])` (400) | The caller's session is fine; a 401 here reads to a client as "you were signed out", which is wrong and causes spurious re-auth loops |
| `Coms/Commands/ReceiveWebhook/ReceiveWebhookHandler.cs:46` | 400 no signing key | `InvalidOperationException("No signing key is configured for provider '…'.")` → 500 | A missing key is OUR misconfiguration, not the caller's malformed request |
| `Coms/Commands/ReceiveWebhook/ReceiveWebhookHandler.cs:62` | 400 body not JSON | `ValidationException([new ValidationFailure("body", "Webhook body is not valid JSON.")])` (400) | |
| `Coms/Commands/SendEmail/SendEmailHandler.cs:45` | 400 template data invalid | `ValidationException(schemaIssues.Select(i => new ValidationFailure($"data.{i.Path}", i.Message)))` (400) | The schema issues already carry paths — they become per-field errors instead of one prose string |
| `Docs/Commands/UploadDocuments/UploadDocumentsHandler.cs:48,68` | raw 4xx | `ValidationException([new ValidationFailure("files", …)])` (400) | |
| `Api/Docs/DocsEndpoints.cs:196` | 400 unexpected multipart field | `ValidationException([new ValidationFailure("files", "Unexpected multipart field '…'; expected 'files'.")])` | |
| `Api/Docs/DocsEndpoints.cs:333` | raw 500 | `InvalidOperationException(...)` | The handler is the mapper now |
| `Api/Http/Validation.cs:766` (`RouteParams.PipedUuid`) | 400 bad uuid | **deleted** — routes gain `{id:guid}` constraints, so a non-GUID id is an unmatched route (404) | Task 5 onward applies the constraint per module |
| `Api/Http/Validation.cs:775` (`RouteParams.UnpipedUuid`) | raw 500 | **deleted** with the file (Task 3) | |
| `Api/Fhir/FhirEndpoints.cs:134`, `Api/Fhir/PatientFhirMapper.cs:157` (`Unprocessable`) | `new ApiException(422, …)` | `new ValidationException([new ValidationFailure(field, message)])`, rendered as 422 by `FhirResponses` only | See Step 7 |

`ApiErrorCode` and its eight constants go with `ApiException.cs`. Grep afterwards:
`grep -rn "ApiException\|ApiErrorCode" src/` must return **zero** hits.

- [ ] **Step 7: Retarget the FHIR renderer**

`src/Hsm.Api/Fhir/FhirResponses.cs` — replace `ExecuteAsync` and `IssueCode`:

```csharp
    /// <summary>
    /// Wraps a FHIR endpoint so application failures render OperationOutcome
    /// instead of problem+json. The FHIR door is the ONE place a validation
    /// failure is not a 400: FHIR clients expect 422 for a resource they sent
    /// that the server could not accept, and the R4 spec's own examples use it.
    /// Everything else is the same closed set, same statuses.
    /// </summary>
    public static async Task ExecuteAsync(HttpContext ctx, Func<Task<IResult>> endpoint)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(endpoint);
        try
        {
            var result = await endpoint();
            await result.ExecuteAsync(ctx);
        }
        catch (Exception exception) when (!ctx.Response.HasStarted)
        {
            var (status, diagnostics) = Classify(exception);
            await WriteOperationOutcomeAsync(ctx, status, diagnostics);
        }
    }

    private static (int Status, string Diagnostics) Classify(Exception exception) => exception switch
    {
        ValidationException validation => (
            StatusCodes.Status422UnprocessableEntity,
            string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
        NotFoundException notFound => (StatusCodes.Status404NotFound, notFound.Message),
        ConflictException conflict => (StatusCodes.Status409Conflict, conflict.Message),
        TooManyRequestsException => (StatusCodes.Status429TooManyRequests, "Too Many Requests"),
        _ => (StatusCodes.Status500InternalServerError, "Internal server error"),
    };

    /// <summary>The HTTP-status → FHIR IssueType map, formerly ErrorStatusCodes.</summary>
    private static string IssueCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not-found",
        StatusCodes.Status409Conflict => "duplicate",
        StatusCodes.Status429TooManyRequests => "processing",
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => "invalid",
        >= StatusCodes.Status500InternalServerError => "exception",
        _ => "processing",
    };
```

The FHIR routes' status codes, body shapes and content types are otherwise unchanged.

- [ ] **Step 8: Leave `ApiEnvelope` standing for `/v1/auth` only**

`ApiEnvelope.Success` still renders the eleven surviving `/v1/auth` routes and every module
route that Tasks 5–10 have not reached yet. Delete from it, in this task, only what depended
on `ApiException`: `IssueFor`, `WriteErrorAsync`, `ValidationIssue` and `CodeForStatus`.
`Success`, `Pagination` and `SinglePagePagination` survive until Task 13 deletes the file.

> This is the single deliberate piece of scaffolding in the plan. It exists because the
> alternative is reshaping the identity surface and the identity *machinery* in one task, with
> no green tree between them. Task 13 Step 8 deletes `src/Hsm.Api/Http/ApiEnvelope.cs` and
> greps for `ApiEnvelope` to prove nothing is left.

- [ ] **Step 9: Retarget the behavior unit tests**

`tests/Hsm.Tests/Abstractions/AuthorizationBehaviorTests.cs`: every
`await Assert.ThrowsAsync<ApiException>(…)` plus `Assert.Equal(401, ex.StatusCode)` becomes
`await Assert.ThrowsAsync<UnauthorizedException>(…)` (or `ForbiddenException`), and the
status assertion is deleted — the type IS the status now. Same edit in
`tests/Hsm.Tests/Abstractions/ValidationBehaviorTests.cs` (Task 3 rewrites that file wholesale).

- [ ] **Step 10: Run the tests**

Run: `dotnet test tests/Hsm.Api.Tests --filter ProblemDetailsTests`
Expected: PASS, 5 tests.

Run: `dotnet test Hsm.sln`
Expected: PASS.

- [ ] **Step 11: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "feat: RFC 9457 problem details and a closed exception set

ApiException let any handler raise any status with any message and rendered a
NestJS envelope nobody is entitled to any more. Five named exceptions with
fixed statuses replace it, mapped by one IExceptionHandler that attaches
traceId centrally. There is no general BadRequestException on purpose: a
shape refusal is a validator's business and a state refusal is a Conflict.
FHIR keeps OperationOutcome and is the one place a validation failure is 422."
```

---

### Task 3: FluentValidation in the pipeline

Two validation systems exist today: a hand-rolled `IValidator<TRequest>` inside the pipeline
and a 776-line `BodyValidator`/`QueryValidator` at the HTTP edge that reproduces NestJS's
`ValidationPipe`. Both go; FluentValidation in the pipeline is the only one left, so HTTP,
Blazor and queued dispatch validate identically.

**Files:**
- Modify: `Directory.Packages.props`, `src/Hsm.Application/Hsm.Application.csproj`,
  `src/Hsm.Application/Abstractions/Behaviors/ValidationBehavior.cs`,
  `src/Hsm.Application/Abstractions/PipelineRegistration.cs`,
  `src/Hsm.Infrastructure/DependencyInjection.cs`
- Create: `src/Hsm.Application/Abstractions/PagingRules.cs`,
  `src/Hsm.Application/Users/Commands/CreateStaffUser/CreateStaffUserValidator.cs` (rewritten),
  `src/Hsm.Application/Users/Commands/ChangeUserRole/ChangeUserRoleValidator.cs`,
  and the validators listed in Step 6's table
- Delete: `src/Hsm.Application/Abstractions/IValidator.cs`,
  `src/Hsm.Api/Http/Validation.cs` (776 lines)
- Test: `tests/Hsm.Tests/Abstractions/ValidationBehaviorTests.cs` (rewritten),
  `tests/Hsm.Api.Tests/Errors/ValidationProblemTests.cs`

**Interfaces:**
- Consumes: `IPipelineBehavior<,>`, `FluentValidation.IValidator<T>`,
  `FluentValidation.ValidationException`.
- Produces:
  - `ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)` where
    `IValidator<T>` is now **FluentValidation's**
  - `static class PagingRules` — `IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int>)`,
    `IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int>)`
  - `IServiceCollection AddHsmValidators(this IServiceCollection)` — assembly scan

- [ ] **Step 1: Add the packages**

```bash
dotnet add src/Hsm.Application package FluentValidation
dotnet add src/Hsm.Application package FluentValidation.DependencyInjectionExtensions
```

The commands write `<PackageVersion Include="FluentValidation" Version="…" />` entries into
`Directory.Packages.props` and versionless `<PackageReference>` entries into
`Hsm.Application.csproj`. **Verify** that the csproj lines carry no `Version` attribute
before continuing — central package management is a global constraint, and `dotnet add
package` occasionally writes one.

- [ ] **Step 2: Rewrite the behavior test (it currently exercises the hand-rolled type)**

`tests/Hsm.Tests/Abstractions/ValidationBehaviorTests.cs`, replacing the whole file:

```csharp
using FluentValidation;
using Hsm.Application.Abstractions;
using Hsm.Application.Abstractions.Behaviors;

namespace Hsm.Tests.Abstractions;

public class ValidationBehaviorTests
{
    private sealed record Create(string Name, int Page) : ICommand<string>;

    private sealed class CreateValidator : AbstractValidator<Create>
    {
        public CreateValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Page).ValidPage();
        }
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);

        var result = await behavior.HandleAsync(
            new Create("ok", 1), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task Invalid_request_throws_before_the_handler_naming_every_bad_field()
    {
        var behavior = new ValidationBehavior<Create, string>([new CreateValidator()]);
        var reached = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.HandleAsync(
            new Create("  ", 0),
            () => { reached = true; return Task.FromResult("done"); },
            CancellationToken.None));

        Assert.False(reached);
        Assert.Contains(exception.Errors, e => e.PropertyName == "Name");
        Assert.Contains(exception.Errors, e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task Every_registered_validator_runs_and_failures_are_merged()
    {
        var behavior = new ValidationBehavior<Create, string>(
            [new CreateValidator(), new CreateValidator()]);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.HandleAsync(
            new Create(string.Empty, 1), () => Task.FromResult("done"), CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count(e => e.PropertyName == "Name"));
    }

    [Fact]
    public async Task No_validator_registered_is_not_an_error()
    {
        var behavior = new ValidationBehavior<Create, string>([]);

        var result = await behavior.HandleAsync(
            new Create(string.Empty, 0), () => Task.FromResult("done"), CancellationToken.None);

        Assert.Equal("done", result);
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter ValidationBehaviorTests`
Expected: FAIL — `ValidPage()` does not exist and `ValidationBehavior` still resolves the
hand-rolled `Hsm.Application.Abstractions.IValidator<T>`.

- [ ] **Step 4: Rewrite `ValidationBehavior`**

`src/Hsm.Application/Abstractions/Behaviors/ValidationBehavior.cs`:

```csharp
using FluentValidation;
using FluentValidation.Results;

namespace Hsm.Application.Abstractions.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request type and
/// throws with the merged failures. This is validation's ONLY home: an HTTP
/// caller, a Blazor circuit and a queued job all reach the handler through this
/// stage, so a rule written once holds on every path. It sits behind
/// authorization on purpose — a caller who may not perform the action is
/// refused before the system spends work checking their payload.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(context, ct).ConfigureAwait(false);
            failures.AddRange(result.Errors);
        }

        // Every validator runs before anything throws, so a caller sees all of
        // their mistakes at once rather than one per round trip.
        return failures.Count > 0 ? throw new ValidationException(failures) : await next().ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Write `PagingRules` and the scan registration**

`src/Hsm.Application/Abstractions/PagingRules.cs`:

```csharp
using FluentValidation;

namespace Hsm.Application.Abstractions;

/// <summary>
/// The paging contract, stated once. Every paged query's validator calls these
/// two rules, so "page starts at 1" and "pageSize is capped at 100" cannot
/// drift between modules — and an oversized pageSize is REFUSED rather than
/// silently clamped, because a caller who asked for 5000 rows and got 100
/// without being told will page incorrectly.
/// </summary>
public static class PagingRules
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static IRuleBuilderOptions<T, int> ValidPage<T>(this IRuleBuilder<T, int> rule) =>
        rule.GreaterThanOrEqualTo(1).WithMessage("page must be 1 or greater.");

    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, MaxPageSize)
            .WithMessage($"pageSize must be between 1 and {MaxPageSize}.");
}
```

In `src/Hsm.Application/Abstractions/PipelineRegistration.cs`, add a scan so a new validator
never needs a registration line:

```csharp
using System.Reflection;
using FluentValidation;
…
    public static IServiceCollection AddHsmPipeline(this IServiceCollection services)
    {
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services.AddHsmValidators();
    }

    /// <summary>
    /// Every AbstractValidator in Hsm.Application, registered as
    /// IValidator&lt;T&gt;. Scanning rather than listing is deliberate: a
    /// validator that exists but was never registered is a rule that silently
    /// does not run, which is the worst failure mode available here.
    /// </summary>
    public static IServiceCollection AddHsmValidators(this IServiceCollection services) =>
        services.AddValidatorsFromAssembly(
            Assembly.GetAssembly(typeof(PipelineRegistration))!,
            ServiceLifetime.Scoped,
            includeInternalTypes: false);
```

Delete the individual `AddScoped<IValidator<CreateStaffUserCommand>, …>()` lines from
`src/Hsm.Infrastructure/DependencyInjection.cs`.

- [ ] **Step 6: Port the surviving rules**

`src/Hsm.Api/Http/Validation.cs` reproduced NestJS's `ValidationPipe`, including
`forbidNonWhitelisted` (reject unknown fields) and per-constraint keys. Almost all of it dies
with the frozen contract. **These rules survive** — they are business rules, not envelope
reproduction, and each becomes a command validator:

| Rule (current location) | New validator | Rule |
|---|---|---|
| Staff role must not be patient-facing (`CreateStaffUserHandler.cs:27`) | `CreateStaffUserValidator` | `RuleFor(x => x.Role).Must(RoleCatalog.IsAssignableToStaff)` |
| Role must be a known role (`UserEndpoints.CreateStaff`, `@IsIn(ROLE_VALUES)`) | `CreateStaffUserValidator` | `RuleFor(x => x.Role).Must(RoleCatalog.IsKnown)` |
| Role must be a known role (`ChangeUserRoleHandler.cs:19`) | `ChangeUserRoleValidator` | `RuleFor(x => x.Role).Must(RoleCatalog.IsKnown)` |
| Temp password ≥ 8 chars (`UserEndpoints.CreateStaff`) | `CreateStaffUserValidator` | `RuleFor(x => x.TempPassword).MinimumLength(8)` |
| Email must be an email (`UserEndpoints.CreateStaff`) | `CreateStaffUserValidator` | `RuleFor(x => x.Email).EmailAddress()` |
| Username / firstName / firstLastName required | `CreateStaffUserValidator` | `.NotEmpty()` on each |
| New password ≥ 8 chars (`UserEndpoints.ChangeOwnPassword`) | `ChangeOwnPasswordValidator` | `RuleFor(x => x.NewPassword).MinimumLength(8)`; `CurrentPassword` `.NotEmpty()` |
| firstName/email non-empty when present (`UserEndpoints.UpdateOwnProfile`) | `UpdateOwnProfileValidator` | `RuleFor(x => x.FirstName).NotEmpty().When(x => x.FirstName is not null)` and the same for `Email` plus `.EmailAddress()` |
| page ≥ 1, limit 1..100 (`UserEndpoints.ListUsers`) | `ListUsersValidator` | `ValidPage()` / `ValidPageSize()` |
| Settings category must be a known category (`SettingsEndpoints`) | `GetSettingsValidator`, `UpdateSettingsValidator` | `RuleFor(x => x.Category).Must(SettingCatalog.IsKnown)` |
| Each settings item needs a non-empty key (`SettingsEndpoints.ValidateItem`) | `UpdateSettingsValidator` | `RuleForEach(x => x.Updates).ChildRules(i => i.RuleFor(u => u.Key).NotEmpty())` |
| Template payload required fields (`TemplateEndpoints.ReadTemplateBodyAsync`) | `CreateTemplateValidator`, `UpdateTemplateValidator` | `.NotEmpty()` on `Payload.Name`, `Payload.Identifier`, `Payload.Category`; `Category` `.Must(TemplateCatalog.IsKnownCategory)` |
| Draft render needs content (`TemplateEndpoints.DraftRender`) | `DraftRenderValidator` | `RuleFor(x => x.Content).NotEmpty()` |
| Recipient emails must be emails (`ComsEndpoints.SendEmail`) | `SendEmailValidator` | `RuleForEach(x => x.To).EmailAddress()`; `RuleFor(x => x.EmailTemplate).NotEmpty()` |
| Document list page/limit (`DocsEndpoints.ListDocuments`) | `ListDocumentsValidator` | `ValidPage()` / `ValidPageSize()` |
| Presign `expiresInSeconds` bounds (`DocsEndpoints.GetDocumentsUrl`) | `PresignDocumentsValidator` | `RuleFor(x => x.ExpiresInSeconds).InclusiveBetween(1, 604_800).When(x => x.ExpiresInSeconds.HasValue)` |
| Upload must carry at least one file (`DocsEndpoints.UploadDocuments`) | `UploadDocumentsValidator` | `RuleFor(x => x.Files).NotEmpty()` |
| Generate needs template + title (`DocsEndpoints.GenerateDocument`) | `GenerateDocumentValidator` | `.NotEmpty()` on `TemplateIdentifier` and `Title` |
| Audit list limit (`SettingsEndpoints`, `ListSettingsAuditQuery.Limit`) | `ListSettingsAuditValidator` | `ValidPage()` / `ValidPageSize()` after Task 4 reshapes it |

**Deliberately NOT ported:** `RejectUnknownFields`/`RejectUnknownParams`
(`forbidNonWhitelisted`). System.Text.Json ignores unknown members by default and an unknown
query parameter is not an error in any standard API. Refusing them existed only to match
NestJS's `ValidationPipe`. This is a behavior change and it is intentional — record it in the
conventions doc (Task 17).

The reference validator, written in full because the later ones follow it exactly:

```csharp
// src/Hsm.Application/Users/Commands/CreateStaffUser/CreateStaffUserValidator.cs
using FluentValidation;
using Hsm.Domain.Identity;

namespace Hsm.Application.Users.Commands.CreateStaffUser;

/// <summary>
/// Shape and business rules for provisioning a staff account. The
/// patient/family exclusion is the one rule here that is not obvious: those
/// accounts are created through the patient registration path, never by an
/// admin provisioning staff, so handing one of those roles to this command is a
/// caller mistake and not a permission problem.
/// </summary>
public sealed class CreateStaffUserValidator : AbstractValidator<CreateStaffUserCommand>
{
    public CreateStaffUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.FirstLastName).NotEmpty();
        RuleFor(x => x.TempPassword).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(RoleCatalog.IsKnown).WithMessage("role is not a known role.")
            .Must(RoleCatalog.IsAssignableToStaff)
                .WithMessage("role must be a staff role; patient accounts are created by registration.");
    }
}
```

- [ ] **Step 7: Delete the edge validators**

```bash
git rm src/Hsm.Api/Http/Validation.cs
git rm src/Hsm.Application/Abstractions/IValidator.cs
```

Every endpoint that called `BodyValidator.ReadAsync(ctx)` now binds the request record
directly from the JSON body. Tasks 5–10 rewrite those endpoints module by module; **in this
task**, change each endpoint's body reading to `await ctx.Request.ReadFromJsonAsync<T>(...)`
into the command record and leave its route and response shape alone. A malformed JSON body
now produces ASP.NET's built-in `BadHttpRequestException` → 400 problem, which is the
framework behavior we want and no longer our code.

- [ ] **Step 8: Write the API-level validation shape test**

`tests/Hsm.Api.Tests/Errors/ValidationProblemTests.cs`:

```csharp
using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Errors;

public sealed class ValidationProblemFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_validation";
}

public class ValidationProblemTests(ValidationProblemFactory factory)
    : IClassFixture<ValidationProblemFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Validation_failures_render_camelCase_field_keys()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(
            "/v1/user/staff",
            new
            {
                username = "",
                email = "not-an-email",
                firstName = "A",
                firstLastName = "B",
                role = Roles.Patient,
                tempPassword = "short",
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("username", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("role", out _));
        Assert.True(errors.TryGetProperty("tempPassword", out _));
        // PascalCase property names must NOT leak onto the wire.
        Assert.False(errors.TryGetProperty("Username", out _));
    }

    [Fact]
    public async Task All_failures_are_reported_in_one_response()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync(
            "/v1/user/staff",
            new
            {
                username = "",
                email = "",
                firstName = "",
                firstLastName = "",
                role = "",
                tempPassword = "",
            },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").EnumerateObject().Count() >= 5);
    }
}
```

- [ ] **Step 9: Run everything**

Run: `dotnet test Hsm.sln`
Expected: PASS.

- [ ] **Step 10: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: FluentValidation is the only validation system

The hand-rolled pipeline IValidator and the 776-line edge BodyValidator that
reproduced NestJS's ValidationPipe are both deleted. Validators are
AbstractValidator<TCommand>, assembly-scanned, executed by the pipeline — so
an HTTP caller, a Blazor circuit and a queued job are validated by the same
rule. Failures render ValidationProblemDetails with camelCase field keys.
Unknown-field rejection (forbidNonWhitelisted) is deliberately not ported."
```

---

### Task 4: `PagedResult<T>` and the paged-query reshape

**Files:**
- Create: `src/Hsm.Contracts/PagedResult.cs`
- Modify: `src/Hsm.Application/Users/Queries/ListUsers/{ListUsersQuery,ListUsersHandler}.cs`,
  `src/Hsm.Application/Coms/Queries/ListEmailBatches/*`,
  `src/Hsm.Application/Docs/Queries/ListDocuments/*`,
  `src/Hsm.Application/Settings/Queries/ListSettingsAudit/*`,
  `src/Hsm.Application/Coms/IEmailBatchStore.cs`, `src/Hsm.Application/Docs/IDocumentStore.cs`,
  `src/Hsm.Application/Settings/ISettingsAuditStore.cs`,
  `src/Hsm.Application/Auth/IUserStore.cs`,
  the matching stores in `src/Hsm.Infrastructure/{Identity,Coms,Docs,Settings}/`,
  the endpoints and UI services that consume them
- Delete: `src/Hsm.Application/Coms/Queries/ListEmailRecipients/` (query, handler, filter)
- Test: `tests/Hsm.Tests/Abstractions/PagedResultTests.cs`

**Interfaces:**
- Consumes: `PagingRules` (Task 3).
- Produces:
  - `sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)`
    with computed `int TotalPages`
  - Reshaped queries (the four surviving paged ones):
    - `[RequireRole(Roles.Admin)] sealed record ListUsersQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<User>>`
    - `sealed record ListEmailsQuery(EmailListFilter Filter, int Page = 1, int PageSize = 20) : IQuery<PagedResult<EmailBatch>>`
    - `sealed record ListDocumentsQuery(DocumentListFilter Filter, int Page = 1, int PageSize = 20) : IQuery<PagedResult<Document>>`
    - `[RequireRole(Roles.Admin)] sealed record ListSettingsAuditQuery(string Category, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AppSettingAudit>>`

- [ ] **Step 1: Write the failing unit test**

`tests/Hsm.Tests/Abstractions/PagedResultTests.cs`:

```csharp
using Hsm.Contracts;

namespace Hsm.Tests.Abstractions;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(100, 7, 15)]
    public void TotalPages_is_the_ceiling_of_total_over_page_size(int totalItems, int pageSize, int expected)
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: pageSize, TotalItems: totalItems);

        Assert.Equal(expected, result.TotalPages);
    }

    [Fact]
    public void A_zero_page_size_reports_no_pages_rather_than_dividing_by_zero()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 0, TotalItems: 5);

        Assert.Equal(0, result.TotalPages);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Tests --filter PagedResultTests`
Expected: FAIL — `Hsm.Contracts.PagedResult<T>` does not exist.

- [ ] **Step 3: Write `PagedResult<T>`**

`src/Hsm.Contracts/PagedResult.cs`:

```csharp
namespace Hsm.Contracts;

/// <summary>
/// One page of a collection, and the shape every paged endpoint returns.
///
/// <para><see cref="TotalPages"/> is computed rather than carried: it is a pure
/// function of the other two numbers, and a stored copy is a field that can
/// disagree with them. It lives in Hsm.Contracts because both the REST door and
/// the Blazor UI services hand it to callers, and Hsm.Contracts is the only
/// assembly both can see.</para>
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalItems / (double)PageSize) : 0;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);

    /// <summary>Projects the items, keeping the paging numbers — endpoints use this
    /// to turn a page of entities into a page of resources.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PagedResult<TOut>([.. Items.Select(selector)], Page, PageSize, TotalItems);
    }
}
```

- [ ] **Step 4: Reshape the four paged queries and their stores**

The complete conversion. `limit` becomes `pageSize` on the wire and in every signature; the
defaults are `page = 1`, `pageSize = 20`; the cap is 100 and is **enforced by a validator**
(Task 3's `ValidPageSize`), so an over-cap request is a 400 rather than a silent clamp.

| Query | Was | Becomes |
|---|---|---|
| `ListUsersQuery(int Page, int Limit) : IQuery<ListUsersResult>` | `ListUsersResult(IReadOnlyList<User> Users, int Page, int PageSize, int TotalItems)` | `ListUsersQuery(int Page = 1, int PageSize = 20) : IQuery<PagedResult<User>>`; `ListUsersResult` **deleted** |
| `ListEmailBatchesQuery(BatchListFilter Filter) : IQuery<IReadOnlyList<EmailBatch>>` (paging lived inside `BatchListFilter`) | filter carried `Page`/`Limit` | `ListEmailsQuery(EmailListFilter Filter, int Page = 1, int PageSize = 20) : IQuery<PagedResult<EmailBatch>>`; `BatchListFilter` renamed `EmailListFilter` and **loses** its `Page`/`Limit` members |
| `ListDocumentsQuery(DocumentListFilter Filter) : IQuery<ListDocumentsResult>` | `ListDocumentsResult(IReadOnlyList<Document> Items, int Total)` | `ListDocumentsQuery(DocumentListFilter Filter, int Page = 1, int PageSize = 20) : IQuery<PagedResult<Document>>`; `ListDocumentsResult` **deleted**; `DocumentListFilter` loses `Page`/`Limit` |
| `ListSettingsAuditQuery(string Category, int Limit = 50) : IQuery<IReadOnlyList<AppSettingAudit>>` | unpaged, capped at 50 | `ListSettingsAuditQuery(string Category, int Page = 1, int PageSize = 20) : IQuery<PagedResult<AppSettingAudit>>` |
| `ListEmailRecipientsQuery(RecipientListFilter Filter)` | paged list of recipients | **DELETED** — the design verified no consumer; recipient statuses ride inside the email detail response (Task 6) |

Store signatures follow:

| Port | Was | Becomes |
|---|---|---|
| `IUserStore.ListAsync` | `Task<(IReadOnlyList<User> Users, int TotalItems)> ListAsync(int page, int limit, CancellationToken)` | `Task<PagedResult<User>> ListAsync(int page, int pageSize, CancellationToken)` |
| `IEmailBatchStore.ListBatchesAsync` | `Task<IReadOnlyList<EmailBatch>> ListBatchesAsync(BatchListFilter, CancellationToken)` | `Task<PagedResult<EmailBatch>> ListEmailsAsync(EmailListFilter, int page, int pageSize, CancellationToken)` |
| `IEmailBatchStore.ListRecipientsAsync` | paged recipients | **deleted** — `GetBatchAsync` already loads recipients |
| `IDocumentStore.ListAsync` | `Task<(IReadOnlyList<Document>, int Total)>` | `Task<PagedResult<Document>> ListAsync(DocumentListFilter, int page, int pageSize, CancellationToken)` |
| `ISettingsAuditStore.ListAsync` | `Task<IReadOnlyList<AppSettingAudit>> ListAsync(string category, int limit, CancellationToken)` | `Task<PagedResult<AppSettingAudit>> ListAsync(string category, int page, int pageSize, CancellationToken)` |

Each EF implementation gains the standard two-query shape — `CountAsync` then
`Skip((page - 1) * pageSize).Take(pageSize)` — over the same `IQueryable` it builds today,
with the same ordering. **Do not change any ordering:** the list ordering is what makes paging
stable, and a changed `OrderBy` silently duplicates or drops rows across pages.

- [ ] **Step 5: Update the consumers**

Endpoints still render the old envelope in this task; they change from
`ApiEnvelope.Pagination(result.Page, result.PageSize, result.TotalItems)` to the same call
against the `PagedResult<T>`'s properties. Tasks 5–9 replace them with the plain
`PagedResult<T>` body. `src/Hsm.Web/Services/{UsersAdminUiService,DocumentsAdminUiService,SettingsAdminUiService}.cs`
change their return types to `PagedResult<T>` and the matching `Hsm.Contracts/Ui/I*.cs`
interfaces change with them (`Hsm.Contracts` can now name `PagedResult<T>` because it is in
the same assembly).

- [ ] **Step 6: Run everything**

Run: `dotnet test Hsm.sln`
Expected: PASS.

- [ ] **Step 7: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "feat: PagedResult<T> replaces four bespoke list result shapes

One paged shape — items, page, pageSize, totalItems and a computed
totalPages — across users, emails, documents and the settings audit, with
page/pageSize standardised at 1/20 and capped at 100 by a validator rather
than clamped in silence. ListEmailRecipients is deleted: it had no consumer
and recipient statuses belong inside the email they belong to."
```

---

### Task 5: Users — the reference module

**This is the worked example.** Tasks 6–10 repeat this exact procedure and give conversion
tables instead of repeating the code.

**Files:**
- Create: `src/Hsm.Api/Users/UserResource.cs`,
  `tests/Hsm.Api.Tests/Users/UsersEndpointTests.cs`
- Modify: `src/Hsm.Api/Users/UserEndpoints.cs` (rewritten), `src/Hsm.Api/Program.cs`
- Delete: nothing (the module's slices are unchanged)

**Interfaces:**
- Consumes: `PagedResult<T>` (Task 4), the closed exception set (Task 2), the validators
  (Task 3), the six existing Users slices (unchanged).
- Produces:
  - `sealed record UserResource(...)` — the wire shape, projected at the endpoint
  - `sealed record CreateUserRequest(...)`, `sealed record UpdateUserRoleRequest(string Role)`,
    `sealed record UpdateOwnProfileRequest(string? FirstName, string? Email)`,
    `sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword)`
  - route group `/api/v1/users`

**Route map for this module:**

| Method | Route | Dispatches | Success | Policy |
|---|---|---|---|---|
| GET | `/api/v1/users` | `ListUsersQuery(page, pageSize)` | 200 `PagedResult<UserResource>` | admin |
| POST | `/api/v1/users` | `CreateStaffUserCommand(...)` | 201 + `Location: /api/v1/users/{id}`, `UserResource` | admin |
| GET | `/api/v1/users/{id:guid}` | `GetUserQuery(id)` | 200 `UserResource` | admin |
| PATCH | `/api/v1/users/{id:guid}` | `ChangeUserRoleCommand(id, role)` | 200 `UserResource` | admin |
| PATCH | `/api/v1/users/me` | `UpdateOwnProfileCommand(...)` | 200 `UserResource` | authenticated |
| POST | `/api/v1/users/me/password` | `ChangeOwnPasswordCommand(...)` | 204 | authenticated |

`POST /users` replaces `POST /user/staff`: creating a user *is* the collection's POST, and the
"staff" qualifier lives in the validator's role rule, not in the path. `PATCH /users/{id}`
replaces `PATCH /user/{id}/role`: the role is a property of the user, so it is patched at the
user, and if a second patchable property ever appears the route does not change.
`{id:guid}` means a non-GUID id is an unmatched route — a 404, not the frozen system's 500.

- [ ] **Step 1: Write the failing module tests**

`tests/Hsm.Api.Tests/Users/UsersEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Users;

public sealed class UsersFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_users";
}

public class UsersEndpointTests(UsersFactory factory) : IClassFixture<UsersFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewUserBody(string username) => new
    {
        username,
        email = $"{username}@api.test",
        firstName = "Ada",
        firstLastName = "Lovelace",
        role = Roles.Nurse,
        tempPassword = "Temp-Passw0rd",
    };

    [Fact]
    public async Task Create_returns_201_with_a_location_header_and_the_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];

        var response = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/users/{id}", response.Headers.Location?.ToString());
        Assert.Equal(username, created.GetProperty("username").GetString());
        Assert.Contains(Roles.Nurse, created.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        // The password hash and the soft-delete marker never reach the wire.
        Assert.False(created.TryGetProperty("passwordHash", out _));
        Assert.False(created.TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public async Task List_is_paged_and_reports_its_totals()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync(
            "/api/v1/users?page=1&pageSize=2", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.True(page.GetProperty("totalItems").GetInt32() >= 1);
        Assert.True(page.GetProperty("totalPages").GetInt32() >= 1);
        Assert.True(page.GetProperty("items").GetArrayLength() <= 2);
    }

    [Fact]
    public async Task Page_size_above_the_cap_is_refused_not_clamped()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=500", CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("pageSize", out _));
    }

    [Fact]
    public async Task Patching_a_users_role_returns_the_updated_resource()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        var username = $"u{Guid.NewGuid():N}"[..20];
        var created = await client.PostAsJsonAsync(
            "/api/v1/users", NewUserBody(username), CancellationToken.None);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None))
            .GetProperty("id").GetGuid();

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/users/{id}", new { role = Roles.Doctor }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        Assert.Contains(Roles.Doctor, updated.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Changing_your_own_password_returns_204()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = UsersFactory.SeedPassword, newPassword = "New-Passw0rd" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_current_password_is_a_field_validation_failure()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = "not-the-password", newPassword = "New-Passw0rd" },
            CancellationToken.None);

        var problem = await ProblemAssert.ProblemAsync(response, 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty("currentPassword", out _));
    }

    [Theory]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("POST", "/api/v1/users")]
    public async Task Anonymous_callers_get_401(string method, string route)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route)
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task A_non_admin_gets_403_on_the_admin_collection()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task A_non_guid_id_is_an_unmatched_route()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users/not-a-guid", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_user_id_is_a_404_problem()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 404);
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/Hsm.Api.Tests --filter UsersEndpointTests`
Expected: FAIL — every route 404s; `/api/v1/users` does not exist.

- [ ] **Step 3: Write the resource and request records**

`src/Hsm.Api/Users/UserResource.cs`:

```csharp
using Hsm.Domain.Identity;

namespace Hsm.Api.Users;

/// <summary>
/// The wire shape of a user. Roles flatten to a string array: the role ROW's
/// id, domain and creation timestamp are internal bookkeeping and no caller has
/// ever had a use for them. PasswordHash and DeletedAt are absent by
/// construction — this record is the allow-list, so a new column on the entity
/// cannot leak by being forgotten.
/// </summary>
public sealed record UserResource(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string? Gender,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? OnboardingCompletedAt,
    bool IsActive,
    bool EmailVerified,
    bool PhoneVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UserResource From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserResource(
            user.Id,
            user.Username,
            user.Email,
            user.FirstName,
            user.SecondName,
            user.FirstLastName,
            user.SecondLastName,
            user.PhoneNumber,
            user.Gender,
            [.. user.Roles.Select(r => r.Role)],
            user.LastLoginAt,
            user.OnboardingCompletedAt,
            user.IsActive,
            user.EmailVerified,
            user.PhoneVerified,
            user.CreatedAt,
            user.UpdatedAt);
    }
}

/// <summary>Request bodies. Bound by System.Text.Json; validated by the pipeline.</summary>
public sealed record CreateUserRequest(
    string Username,
    string Email,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? PhoneNumber,
    string Role,
    string TempPassword);

public sealed record UpdateUserRoleRequest(string Role);

public sealed record UpdateOwnProfileRequest(string? FirstName, string? Email);

public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
```

- [ ] **Step 4: Rewrite the endpoints**

`src/Hsm.Api/Users/UserEndpoints.cs`, replacing the whole file:

```csharp
using Hsm.Application.Abstractions;
using Hsm.Application.Users.Commands.ChangeOwnPassword;
using Hsm.Application.Users.Commands.ChangeUserRole;
using Hsm.Application.Users.Commands.CreateStaffUser;
using Hsm.Application.Users.Commands.UpdateOwnProfile;
using Hsm.Application.Users.Queries.GetUser;
using Hsm.Application.Users.Queries.ListUsers;
using Hsm.Contracts;

namespace Hsm.Api.Users;

/// <summary>
/// The users resource. Every delegate does transport work only — bind, dispatch,
/// project, choose a status. There is no authentication call and no role check
/// here: the actor is installed by middleware and the policy rides on the
/// request type, so an endpoint cannot fail open by forgetting either.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var users = app.MapGroup("/api/v1/users").WithTags("Users");

        users.MapGet("/", ListUsers)
            .WithSummary("List users, newest first.")
            .Produces<PagedResult<UserResource>>();

        users.MapPost("/", CreateUser)
            .WithSummary("Create a staff user.")
            .Produces<UserResource>(StatusCodes.Status201Created);

        // /me is registered before /{id:guid} for readability only — the guid
        // constraint means "me" could never match the parameterised route.
        users.MapPatch("/me", UpdateOwnProfile)
            .WithSummary("Update the calling user's own profile.")
            .Produces<UserResource>();

        users.MapPost("/me/password", ChangeOwnPassword)
            .WithSummary("Change the calling user's own password.")
            .Produces(StatusCodes.Status204NoContent);

        users.MapGet("/{id:guid}", GetUser)
            .WithSummary("Read one user.")
            .Produces<UserResource>();

        users.MapPatch("/{id:guid}", UpdateUser)
            .WithSummary("Update a user's role.")
            .Produces<UserResource>();
    }

    private static async Task<IResult> ListUsers(
        IDispatcher dispatcher,
        CancellationToken ct,
        int page = PagingRules.DefaultPage,
        int pageSize = PagingRules.DefaultPageSize)
    {
        var result = await dispatcher.Send(new ListUsersQuery(page, pageSize), ct);
        return Results.Ok(result.Map(UserResource.From));
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        var created = await dispatcher.Send(
            new CreateStaffUserCommand(
                request.Username,
                request.Email,
                request.FirstName,
                request.SecondName,
                request.FirstLastName,
                request.SecondLastName,
                request.PhoneNumber,
                request.Role,
                request.TempPassword),
            ct);
        return Results.Created($"/api/v1/users/{created.Id}", UserResource.From(created));
    }

    private static async Task<IResult> GetUser(Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(await dispatcher.Send(new GetUserQuery(id), ct)));

    private static async Task<IResult> UpdateUser(
        Guid id, UpdateUserRoleRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(
            await dispatcher.Send(new ChangeUserRoleCommand(id, request.Role), ct)));

    private static async Task<IResult> UpdateOwnProfile(
        UpdateOwnProfileRequest request, IDispatcher dispatcher, CancellationToken ct) =>
        Results.Ok(UserResource.From(await dispatcher.Send(
            new UpdateOwnProfileCommand(request.FirstName, request.Email), ct)));

    private static async Task<IResult> ChangeOwnPassword(
        ChangeOwnPasswordRequest request, IDispatcher dispatcher, CancellationToken ct)
    {
        await dispatcher.Send(
            new ChangeOwnPasswordCommand(request.CurrentPassword, request.NewPassword), ct);
        return Results.NoContent();
    }
}
```

Because the endpoints no longer call `RequestAuth.GateAsync`, the actor must be installed for
them. Until Task 12's middleware exists, add this **temporary** line at the top of
`Program.cs`'s middleware chain (Task 12 deletes it and replaces it with `UseHsmActor()`):

```csharp
// Transitional: installs the actor for the reshaped /api routes while the old
// per-endpoint GateAsync still serves the un-reshaped /v1 ones. Task 12
// replaces this whole block with UseAuthentication() + UseHsmActor().
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        && RequestAuth.AccessToken(ctx) is not null)
    {
        var principal = await RequestAuth.AuthenticateAsync(ctx, TokenKind.Access);
        await RequestAuth.InstallActorAsync(ctx, principal);
    }

    await next();
});
```

An absent or invalid token leaves the actor null, which the pipeline answers with 401 — the
fail-closed property is preserved through the transition. An *invalid* token throws
`UnauthorizedException` from `AuthenticateAsync`, which the exception handler renders as 401.

- [ ] **Step 5: Run the module tests**

Run: `dotnet test tests/Hsm.Api.Tests --filter UsersEndpointTests`
Expected: PASS, 11 tests.

- [ ] **Step 6: Run everything, format, commit**

Run: `dotnet test Hsm.sln`
Expected: PASS.

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: /api/v1/users as a standard resource

Six enveloped /v1/user routes become six resource routes returning plain
JSON: paged list, 201+Location on create, 204 on password change, and
PATCH /users/{id} instead of PATCH /user/{id}/role because the role is a
property of the user. UserResource is an allow-list, so a new entity column
cannot leak by being forgotten. Reference module for Tasks 6-10."
```

---

### Tasks 6–10: the remaining modules

Each follows **Task 5's procedure exactly**: failing module test first (happy path, status
codes, the 401/403 matrix, validation 400, pagination where applicable) → resource + request
records → rewritten route file → full suite → format → commit. Run them **one module per
task**. The tables below are complete; nothing is left to invent.

Every route file gains `.WithTags("<Module>")` on its group and `.WithSummary(...)` +
`.Produces<T>(...)` per route, which is what Task 16's OpenAPI document is generated from.
Every `{id}` route parameter carries the `:guid` constraint unless the table says otherwise.

### Task 6: Emails and webhooks

The "batch" noun dies: a batch *is* an email send, so the resource is `emails`.

**Files:** create `src/Hsm.Api/Emails/{EmailEndpoints,EmailResource}.cs` and
`src/Hsm.Api/Webhooks/WebhookEndpoints.cs`; delete `src/Hsm.Api/Coms/ComsEndpoints.cs`;
modify `src/Hsm.Api/Program.cs`; test `tests/Hsm.Api.Tests/Emails/EmailsEndpointTests.cs`.

| Old route | New route | Dispatches | Success | Policy |
|---|---|---|---|---|
| `POST /v1/coms/send/email` | `POST /api/v1/emails` | `SendEmailCommand(...)` then `IJobQueue.EnqueueAsync(new DispatchEmailBatchCommand(id, null))` | **202** + `Location: /api/v1/emails/{id}`, body `{ id, jobId }` | authenticated |
| `GET /v1/coms/emails/batches` | `GET /api/v1/emails` | `ListEmailsQuery(filter, page, pageSize)` | 200 `PagedResult<EmailResource>` | authenticated |
| `GET /v1/coms/emails/batches/{id}` | `GET /api/v1/emails/{id:guid}` | `GetEmailBatchQuery(id)` | 200 `EmailDetailResource` (**includes `recipients[]`**) | authenticated |
| `POST /v1/coms/emails/batches/{id}/resend` | `POST /api/v1/emails/{id:guid}/resend` | `ResendEmailBatchCommand(id)` + enqueue | **202** `{ jobId }` | authenticated |
| `POST /v1/coms/emails/recipients/{id}/resend` | `POST /api/v1/emails/{id:guid}/recipients/{recipientId:guid}/resend` | `ResendEmailRecipientCommand(recipientId)` | **202** `{ jobId }` | authenticated |
| `POST /v1/coms/webhooks/{provider}` | `POST /api/v1/webhooks/{provider}` | `ReceiveWebhookCommand(provider, signature, rawBody)` + enqueue per event | 202 `{ received }` | `[AllowAnonymousRequest]` — the HMAC signature is the credential, unchanged |
| `GET /v1/coms/emails/recipients` | **dropped** | — | — | no consumer (design §Route map) |
| `GET /v1/coms/emails/recipients/{id}` | **dropped** | — | — | folded into email detail |
| `POST /v1/coms/send/sms` | **dropped** | — | — | frozen stub; SMS is out of scope |

Resource records (`src/Hsm.Api/Emails/EmailResource.cs`):

```csharp
public sealed record EmailResource(
    Guid Id, string? FromEmail, string? FromName, string EmailTemplate,
    string OverallStatus, int TotalRecipients, int SentCount, int FailedCount,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record EmailDetailResource(
    Guid Id, string? FromEmail, string? FromName, string EmailTemplate,
    string OverallStatus, int TotalRecipients, int SentCount, int FailedCount,
    Guid? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<EmailRecipientResource> Recipients);

public sealed record EmailRecipientResource(
    Guid Id, string ToEmail, string Status, string? ProviderMessageId,
    string? FailureReason, DateTimeOffset? SentAt, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SendEmailRequest(
    string? FromEmail, string? FromName, IReadOnlyList<string> To,
    string EmailTemplate, System.Text.Json.Nodes.JsonObject Data,
    IReadOnlyList<string>? DocumentIds);

public sealed record AcceptedEmailResponse(Guid Id, string JobId);

public sealed record AcceptedJobResponse(string JobId);
```

Module notes: the two resend routes and the send route return **202**, not 201 — the work is
queued and the caller is being told it was accepted, which is what 202 means. The webhook
route stays anonymous and its raw-body reading, HMAC-SHA1 verification and per-event
enqueueing are moved across **unchanged**; do not "tidy" the raw byte read into a JSON bind,
because the signature is computed over the exact bytes.

Auth matrix for the module tests: anonymous → 401 on all `/api/v1/emails*`; anonymous →
**not** 401 on `/api/v1/webhooks/{provider}` (an unsigned body is a 401 from the handler's
signature check, a correctly-signed one is 202).

Commit:

```bash
git add -A && git commit -m "refactor: /api/v1/emails replaces the coms batch surface

A batch IS an email send, so the resource is emails and the send record is
created by POST /api/v1/emails. Recipient statuses ride in the email detail
instead of two flat routes nothing consumed; per-recipient resend survives as
a sub-resource action. Queued work answers 202. Webhooks move to
/api/v1/webhooks/{provider} with their HMAC credential untouched. SMS is
dropped until it is real."
```

### Task 7: Documents

**Files:** create `src/Hsm.Api/Documents/{DocumentEndpoints,DocumentResource}.cs`; delete
`src/Hsm.Api/Docs/DocsEndpoints.cs`; modify `src/Hsm.Api/Program.cs`; test
`tests/Hsm.Api.Tests/Documents/DocumentsEndpointTests.cs`.

| Old route | New route | Dispatches | Success | Policy |
|---|---|---|---|---|
| `GET /v1/docs` | `GET /api/v1/documents` | `ListDocumentsQuery(filter, page, pageSize)` | 200 `PagedResult<DocumentResource>` | authenticated |
| `POST /v1/docs/upload` | `POST /api/v1/documents` (multipart/form-data, field `files`) | `UploadDocumentsCommand(...)` | 201 `UploadResultResource` | authenticated |
| `POST /v1/docs/generate` | `POST /api/v1/documents/generated` | `GenerateDocumentCommand(...)` + enqueue `RenderDocumentCommand` | **202** + `Location: /api/v1/documents/{id}`, body `{ id, jobId }` | authenticated |
| `GET /v1/docs/{id}` | `GET /api/v1/documents/{id:guid}` | `GetDocumentQuery(id)` | 200 `DocumentDetailResource` (with `versions[]`) | authenticated |
| `DELETE /v1/docs/{id}` | `DELETE /api/v1/documents/{id:guid}` | `DeleteDocumentCommand(id)` | **204** | authenticated |
| `DELETE /v1/docs` (ids in body) | `DELETE /api/v1/documents?ids=a,b,c` | `DeleteDocumentCommand(id)` per id | **204** | authenticated |
| `POST /v1/docs/url` | `POST /api/v1/documents/urls` | `PresignDocumentsQuery(...)` | 200 `IReadOnlyList<PresignedItemResource>` | authenticated |
| `GET /v1/docs/{id}/url` | `GET /api/v1/documents/{id:guid}/url` | `GetDocumentUrlQuery(id)` | 200 `{ url }` | authenticated |
| `POST /v1/docs/create` | **dropped** | — | — | verified frozen no-op stub: returned 201 and did nothing |

`POST /documents/generated` rather than `POST /documents/generate`: the path names the
sub-collection of generated documents, not a verb, and the design's PATCH rule
(metadata-only patches at the root, content changes split per-kind) depends on that noun.

Bulk delete moves from a body to `?ids=`: a DELETE with a body is poorly supported by
intermediaries and by every HTTP client. `ids` is comma-separated GUIDs; a malformed entry is
a `ValidationException` on `ids`; an unknown id makes the whole call a 404 (delete is not
partially applied — the handler runs inside `TransactionBehavior`, so one missing id rolls the
batch back).

Resource records:

```csharp
public sealed record DocumentResource(
    Guid Id, string Title, string? Description, string Type, string Status,
    string? EntityId, string? EntityType, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DocumentDetailResource(
    Guid Id, string Title, string? Description, string Type, string Status,
    string? EntityId, string? EntityType, Guid CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<DocumentVersionResource> Versions);

public sealed record DocumentVersionResource(
    Guid Id, int Version, string? Key, long? Size, string? ContentType, DateTimeOffset CreatedAt);

public sealed record UploadedFileResource(string FileId, string Filename, string Key);

public sealed record UploadedItemResource(string Bucket, IReadOnlyList<UploadedFileResource> Files);

public sealed record UploadResultResource(
    IReadOnlyList<UploadedItemResource> Items, IReadOnlyList<Guid> DocumentIds);

public sealed record PresignRequest(
    IReadOnlyList<PresignItemRequest> Items, string? ContentDisposition, int? ExpiresInSeconds);

public sealed record PresignItemRequest(string Bucket, IReadOnlyList<PresignFileRequest> Files);

public sealed record PresignFileRequest(string FolderName, string FileId);

public sealed record PresignedFileResource(string FileId, string Key, string Url);

public sealed record PresignedItemResource(string Bucket, IReadOnlyList<PresignedFileResource> Files);

public sealed record DocumentUrlResource(string Url);

public sealed record AcceptedDocumentResponse(Guid Id, string JobId);
```

Module notes: the upload path keeps **streaming** — read each `IFormFile` with
`OpenReadStream()` straight into `UploadFileUpload.Content`. Do not reintroduce a
`MemoryStream` + `ToArray` copy; the consolidation pass removed one and a 30 MB scan would
put three copies of it on the heap. The multipart field name stays `files`; an unexpected
field is a `ValidationException` on `files` (Task 2's sweep).

Commit:

```bash
git add -A && git commit -m "refactor: /api/v1/documents as a standard resource

Upload is the collection's POST, generation is POST /documents/generated
answering 202 with a Location, deletes answer 204, and bulk delete takes
?ids= instead of a DELETE body no intermediary handles well. The frozen
/docs/create no-op stub is dropped. Uploads still stream."
```

### Task 8: Templates

**Files:** modify `src/Hsm.Api/Templates/TemplateEndpoints.cs` (rewritten), create
`src/Hsm.Api/Templates/TemplateResource.cs`; test
`tests/Hsm.Api.Tests/Templates/TemplatesEndpointTests.cs`.

| Old route | New route | Dispatches | Success | Policy |
|---|---|---|---|---|
| `GET /v1/templates` | `GET /api/v1/templates?category=` | `ListTemplatesQuery(category)` | 200 `IReadOnlyList<TemplateResource>` (**unpaged** — see Scope Boundaries) | authenticated |
| `POST /v1/templates` | `POST /api/v1/templates` | `CreateTemplateCommand(payload)` | 201 + `Location: /api/v1/templates/{id}` | authenticated |
| `GET /v1/templates/{identifier}` | `GET /api/v1/templates/{id}` | `GetTemplateQuery(id)` | 200 `TemplateDetailResource` | authenticated |
| `PUT /v1/templates/{id}` | `PUT /api/v1/templates/{id:guid}` | `UpdateTemplateCommand(id, payload)` | 200 `TemplateDetailResource` | authenticated |
| `DELETE /v1/templates/{id}` | `DELETE /api/v1/templates/{id:guid}` | `DeleteTemplateCommand(id)` | **204** | authenticated |
| `POST /v1/templates/validate` | `POST /api/v1/templates/validate` | `ValidateTemplateQuery(identifier, data)` | **200** `ValidateTemplateResource` | authenticated |
| `POST /v1/templates/draft-render` | `POST /api/v1/templates/draft-render` | `DraftRenderQuery(content, baseTemplateId, sampleData)` | **200** `{ html }` | authenticated |

**The single identifier form.** The frozen surface addressed templates two ways — by GUID on
`PUT`/`DELETE` and by a slug-or-GUID "identifier" on `GET`. The route parameter is now `{id}`
everywhere. `GET /api/v1/templates/{id}` keeps **no** `:guid` constraint, because
`GetTemplateQuery` accepts a slug and a slug is the form the catalog is authored in;
`PUT`/`DELETE` keep `:guid` because they address a stored row. That asymmetry is deliberate
and is the one place `{id}` means two things — record it in the endpoint's doc comment.

The two action routes drop from 201 to **200**: they compute and persist nothing, so "created"
was never true. They stay POST because they carry a body, and a GET with a body is not a thing.

Resource records:

```csharp
public sealed record TemplateResource(
    Guid Id, string Identifier, string Name, string Category, string? Description,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TemplateDetailResource(
    Guid Id, string Identifier, string Name, string Category, string? Description,
    bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    TemplateEmailResource? Email, TemplateDocResource? Doc);

public sealed record TemplateEmailResource(string Subject, string Body, IReadOnlyList<string>? Attachments);

public sealed record TemplateDocResource(string Body, string? Header, string? Footer);

public sealed record ValidateTemplateRequest(string Identifier, System.Text.Json.Nodes.JsonNode? Data);

public sealed record ValidateTemplateResource(
    bool Valid, Guid? TemplateId, IReadOnlyList<TemplateIssueResource> Issues);

public sealed record TemplateIssueResource(string Path, string Message);

public sealed record DraftRenderRequest(
    string Content, string? BaseTemplateId, System.Text.Json.Nodes.JsonObject? SampleData);

public sealed record DraftRenderResource(string Html);
```

Module notes: the in-use deletion check stays in `DeleteTemplateHandler` and now raises
`ConflictException` (Task 2's sweep) — a template still referenced by a document type is a
409, never a validation 400.

Commit:

```bash
git add -A && git commit -m "refactor: /api/v1/templates with one identifier form

CRUD keeps its shape; the route parameter is {id} everywhere, GUID-constrained
where it addresses a row and unconstrained on GET where a catalog slug is the
authored form. Delete answers 204 and the two compute-only actions answer 200
rather than the frozen 201 they never earned. The list stays unpaged: it is a
bounded admin catalog and no consumer asked to page it."
```

### Task 9: Settings and audit

**Files:** modify `src/Hsm.Api/Settings/SettingsEndpoints.cs` (rewritten), create
`src/Hsm.Api/Settings/SettingsResource.cs`; test
`tests/Hsm.Api.Tests/Settings/SettingsEndpointTests.cs`.

| Old route | New route | Dispatches | Success | Policy |
|---|---|---|---|---|
| `GET /v1/settings?category=` | `GET /api/v1/settings?category=` | `GetSettingsQuery(category)` | 200 `SettingsResource` | admin |
| `PUT /v1/settings` | `PUT /api/v1/settings` | `UpdateSettingsCommand(category, updates)` | 200 `SettingsResource` | admin |
| — (no frozen route; UI-service only) | `GET /api/v1/settings/audit?category=&page=&pageSize=` | `ListSettingsAuditQuery(category, page, pageSize)` | 200 `PagedResult<SettingAuditResource>` | admin |

```csharp
public sealed record SettingsResource(string Category, IReadOnlyList<SettingItemResource> Settings);

public sealed record SettingItemResource(
    string Key, string? Value, bool IsSecret, string? Description, DateTimeOffset? UpdatedAt);

public sealed record UpdateSettingsRequest(string Category, IReadOnlyList<SettingUpdateRequest> Settings);

public sealed record SettingUpdateRequest(string Key, string? Value);

public sealed record SettingAuditResource(
    Guid Id, string Category, string Key, string? OldValue, string? NewValue,
    Guid ChangedBy, DateTimeOffset ChangedAt);
```

Module notes: secret masking, blank-secret-means-unchanged, unknown-key-ignored and one audit
row per *effective* change are handler behavior and move across untouched. `SettingItemResource.Value`
is **already masked** by the handler for secret keys — the resource does no masking of its own,
so there is exactly one place that decision is made. The audit route is new on the wire; it
existed only as a UI-service call before, and exposing it is a deliberate part of R1.

Commit:

```bash
git add -A && git commit -m "refactor: /api/v1/settings plus a real audit route

GET/PUT settings lose the envelope; the settings audit, which existed only as
an in-process UI-service call, becomes a paged admin route. Secret masking
still happens in exactly one place, the handler."
```

### Task 10: System and health

**Files:** create `src/Hsm.Api/System/SystemEndpoints.cs`; delete
`src/Hsm.Api/Health/HealthEndpoints.cs`; modify `src/Hsm.Api/Program.cs`; test
`tests/Hsm.Api.Tests/System/SystemEndpointTests.cs` and the Task 1 smoke test.

| Old route | New route | Implementation | Success | Policy |
|---|---|---|---|---|
| `GET /v1/health` | `GET /health` | `app.MapHealthChecks("/health")` — **standard middleware** | 200 `Healthy` (`text/plain`) | anonymous |
| `GET /v1/health/version` | — merged into system status | — | — | — |
| — | `GET /api/v1/system/status` | `GetSystemStatusQuery` (existing, `[AllowAnonymousRequest]`) | 200 `SystemStatusResource` incl. `version` | anonymous |

**Decision: `/health` is LIVENESS ONLY.** `builder.Services.AddHealthChecks();` registers the
service with **no** checks, so the endpoint answers 200 as long as the process can serve a
request. It deliberately does not probe Postgres or Redis: this endpoint is what a container
orchestrator restarts the process on, and restarting a healthy API because the database had a
two-second blip turns a brief degradation into an outage. Dependency state is *reported*, not
*acted on* — that is `GET /api/v1/system/status`, which already surveys the stores and is what
a dashboard reads.

```csharp
public sealed record SystemStatusResource(
    string Version,
    string Environment,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ComponentStatusResource> Components);

public sealed record ComponentStatusResource(string Name, string Status, string? Detail);
```

`version` resolves as it does today: the `API_VERSION` configuration value, then the
assembly's informational version with any `+build` suffix stripped, then `"0.0.0"`.

- [ ] **Step 6 of this task (explicit, because Task 1 depends on it): retarget the smoke test**

In `tests/Hsm.Api.Tests/System/HealthSmokeTests.cs`, change `"/v1/health"` to `"/health"`.

Commit:

```bash
git add -A && git commit -m "refactor: standard /health, /api/v1/system/status for detail

Health becomes MapHealthChecks with no registered checks — liveness only, on
purpose: an orchestrator restarts the process on this endpoint, and killing a
healthy API over a two-second database blip turns degradation into an outage.
Dependency state is reported by /api/v1/system/status, which also absorbs the
frozen /v1/health/version route."
```

---

### Task 11: Identity membership and schema (Identity part 1)

The riskiest task in the plan. It replaces the user entity, the password hasher, the role
rows and the whole schema baseline in one commit, because they cannot be separated: an
`IdentityDbContext` needs `HsmUser`, `HsmUser` needs Identity's schema, and the schema needs a
regenerated migration.

**Files:**
- Create: `src/Hsm.Domain/Identity/HsmUser.cs`,
  `src/Hsm.Infrastructure/Identity/IdentityRegistration.cs`,
  `src/Hsm.Infrastructure/Identity/UserDirectory.cs`
- Modify: `Directory.Packages.props`, `src/Hsm.Domain/Hsm.Domain.csproj`,
  `src/Hsm.Application/Hsm.Application.csproj`,
  `src/Hsm.Infrastructure/Hsm.Infrastructure.csproj`,
  `src/Hsm.Domain/Identity/RoleCatalog.cs` (add `IdFor`),
  `src/Hsm.Infrastructure/Persistence/HsmDbContext.cs`,
  `src/Hsm.Infrastructure/DependencyInjection.cs`,
  `src/Hsm.Infrastructure/Identity/IdentityStores.cs` (shrunk),
  `src/Hsm.Application/Auth/IUserStore.cs` → `IUserDirectory.cs`,
  every handler in Step 6's table, `tests/Hsm.Api.Tests/Support/ApiFactory.cs`,
  `tests/Hsm.Integration.Tests/TestServices.cs`
- Delete: `src/Hsm.Domain/Identity/User.cs` (`User`, `UserRole`),
  `src/Hsm.Domain/Identity/PasswordResetToken.cs`,
  `src/Hsm.Infrastructure/Identity/BcryptPasswordHasher.cs`,
  `src/Hsm.Application/Auth/{IPasswordHasher,IPasswordResetTokenStore,TokenDigests}.cs`,
  `src/Hsm.Infrastructure/Migrations/**` (regenerated in Step 8)
- Test: `tests/Hsm.Integration.Tests/IdentitySchemaTests.cs`

**Interfaces:**
- Consumes: `PagedResult<T>` (Task 4).
- Produces:
  - `class HsmUser : IdentityUser<Guid>` with `FirstName`, `SecondName`, `FirstLastName`,
    `SecondLastName`, `Gender`, `LastLoginAt`, `OnboardingCompletedAt`, `IsActive`,
    `CreatedAt`, `UpdatedAt`, `DeletedAt`
  - `class HsmDbContext : IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>`
  - `interface IUserDirectory` — three members (see Step 5)
  - `static Guid RoleCatalog.IdFor(string role)`
  - `IServiceCollection AddHsmIdentity(this IServiceCollection, IConfiguration)`

- [ ] **Step 1: Add the packages**

```bash
dotnet add src/Hsm.Domain package Microsoft.Extensions.Identity.Stores
dotnet add src/Hsm.Application package Microsoft.Extensions.Identity.Core
dotnet add src/Hsm.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

Pin all three at the `10.0.10` the other Microsoft packages use. Verify the csproj entries
carry no `Version` attribute.

> **`Hsm.Domain` gains its first package reference.** Its rule was "entities only, no *project*
> references", and that still holds — no `Hsm.*` assembly is referenced. One framework package
> is the price of `HsmUser` being the entity rather than a mapped copy of one. The alternative
> is a domain `User`, an infrastructure `HsmUser`, and a mapper between them that has to be
> right in both directions on every write. Task 17 records this in the conventions doc.

- [ ] **Step 2: Write the failing schema test**

`tests/Hsm.Integration.Tests/IdentitySchemaTests.cs`:

```csharp
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Integration.Tests;

public class IdentitySchemaTests
{
    [Fact]
    public async Task Every_catalog_role_is_seeded_as_an_identity_role()
    {
        using var provider = TestServices.Build();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmDbContext>();
        await db.Database.MigrateAsync(CancellationToken.None);

        var seeded = await db.Roles.Select(r => r.Name).ToListAsync(CancellationToken.None);

        Assert.Equal(
            RoleCatalog.All.OrderBy(r => r, StringComparer.Ordinal),
            seeded.OrderBy(r => r, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_user_created_through_UserManager_hashes_with_PBKDF2_and_authenticates()
    {
        using var provider = TestServices.Build();
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = new HsmUser
        {
            UserName = $"u{Guid.NewGuid():N}"[..20],
            Email = $"{Guid.NewGuid():N}@schema.test",
            FirstName = "Ada",
            FirstLastName = "Lovelace",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var created = await users.CreateAsync(user, "Schema-Passw0rd");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        // ASP.NET Core Identity's v3 PBKDF2 format is a base64 blob whose first
        // byte is the format marker 0x01. bcrypt hashes start "$2".
        Assert.StartsWith("AQ", user.PasswordHash, StringComparison.Ordinal);
        Assert.True(await users.CheckPasswordAsync(user, "Schema-Passw0rd"));
        Assert.False(await users.CheckPasswordAsync(user, "wrong"));
    }

    [Fact]
    public async Task Usernames_stay_case_insensitively_unique()
    {
        using var provider = TestServices.Build();
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var name = $"u{Guid.NewGuid():N}"[..20];

        var first = await users.CreateAsync(NewUser(name), "Schema-Passw0rd");
        Assert.True(first.Succeeded);

        var second = await users.CreateAsync(NewUser(name.ToUpperInvariant()), "Schema-Passw0rd");

        Assert.False(second.Succeeded);
    }

    private static HsmUser NewUser(string name) => new()
    {
        UserName = name,
        Email = $"{Guid.NewGuid():N}@schema.test",
        FirstName = "A",
        FirstLastName = "B",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/Hsm.Integration.Tests --filter IdentitySchemaTests`
Expected: FAIL — `HsmUser`, `db.Roles` and `UserManager<HsmUser>` do not exist.

- [ ] **Step 4: Write `HsmUser`**

`src/Hsm.Domain/Identity/HsmUser.cs`:

```csharp
using Microsoft.AspNetCore.Identity;

namespace Hsm.Domain.Identity;

/// <summary>
/// A human account. Everything ASP.NET Core Identity already models — the id,
/// the login name and its normalized form, the email and its normalized form,
/// the password hash, the phone number, the confirmation flags, the security
/// stamp and the lockout counters — comes from <see cref="IdentityUser{TKey}"/>.
/// What is added here is what the hospital cares about and Identity does not.
///
/// <para>The frozen entity's columns map across as: Username → UserName,
/// EmailVerified → EmailConfirmed, PhoneVerified → PhoneNumberConfirmed. They
/// are not duplicated here; a second IsEmailVerified alongside EmailConfirmed
/// is exactly the kind of pair that drifts.</para>
///
/// <para><see cref="OnboardingCompletedAt"/> null marks an admin-created staff
/// account still pending forced first-login onboarding. The database row is
/// authoritative — never the claim; see RequestActorFactory.</para>
/// </summary>
public class HsmUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;

    public string? SecondName { get; set; }

    public string FirstLastName { get; set; } = string.Empty;

    public string? SecondLastName { get; set; }

    public string? Gender { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Null while the account is pending first-login onboarding.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete. Filtered out of the unique indexes, never on the wire.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
```

Add to `src/Hsm.Domain/Identity/RoleCatalog.cs`:

```csharp
    /// <summary>
    /// The Identity role row's id for a role name, derived from the name so it
    /// is the same in every database that ever gets seeded. That determinism is
    /// what lets the seed live in the migration (HasData) instead of in a
    /// startup task two hosts could race.
    /// </summary>
    public static Guid IdFor(string role) =>
        new(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(role)).AsSpan(0, 16));
```

- [ ] **Step 5: Shrink our user port and rename it**

`git mv src/Hsm.Application/Auth/IUserStore.cs src/Hsm.Application/Auth/IUserDirectory.cs`, then:

```csharp
using Hsm.Contracts;
using Hsm.Domain.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// The two things UserManager cannot do, and nothing else.
///
/// <para>It is named Directory rather than Store because
/// <c>Microsoft.AspNetCore.Identity.IUserStore&lt;TUser&gt;</c> is a real type
/// that is in scope in the same files from now on, and two IUserStores in one
/// using-block is a compile error nobody should have to debug twice.</para>
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Scalar onboarding probe for the request actor: whether a LIVE user row
    /// exists and, if so, its onboardingCompletedAt — without loading the
    /// aggregate. A deleted or deactivated account reports not-found, which is
    /// what makes the actor fail closed.
    /// </summary>
    Task<(bool Found, DateTimeOffset? OnboardingCompletedAt)> OnboardingStateAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>Paged listing, newest first, roles attached.</summary>
    Task<PagedResult<HsmUser>> ListAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>The roles held by each of these users, in one round trip.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> RolesForAsync(
        IReadOnlyList<Guid> userIds, CancellationToken ct = default);
}
```

`RolesForAsync` exists because `UserManager.GetRolesAsync` is per-user and a 20-row page would
otherwise issue 21 queries. Implement it in `src/Hsm.Infrastructure/Identity/UserDirectory.cs`
as one join over `UserRoles` and `Roles`.

- [ ] **Step 6: Move each handler onto `UserManager` — the complete table**

| Handler | Was | Becomes |
|---|---|---|
| `Auth/Commands/Login/LoginHandler` | `IUserStore.FindByUsernameAsync` + `IPasswordHasher.Verify` | `UserManager.FindByNameAsync` + `CheckPasswordAsync`; on failure `AccessFailedAsync`, on success `ResetAccessFailedCountAsync`; `IsLockedOutAsync` checked first and answered with the SAME `UnauthorizedException("Invalid username or password.")` as a wrong password |
| `Auth/Commands/Signup/SignupHandler` | build `User`, hash, `IUserStore.AddAsync` | `UserManager.CreateAsync(user, password)` then `AddToRoleAsync(user, Roles.Patient)`; a non-succeeded `IdentityResult` becomes `ConflictException` for `DuplicateUserName`/`DuplicateEmail` and `ValidationException` for password-policy codes |
| `Auth/Commands/CompleteOnboarding/CompleteOnboardingHandler` | `UpdatePasswordAsync` | `UserManager.RemovePasswordAsync` + `AddPasswordAsync`, then set `OnboardingCompletedAt`/`PhoneNumber` and `UpdateAsync` |
| `Auth/Commands/ForgotPassword/ForgotPasswordHandler` | mint token, `IPasswordResetTokenStore.AddAsync`, SHA-256 at rest | `UserManager.GeneratePasswordResetTokenAsync`; the per-account rate limit stays in the handler and still throws `TooManyRequestsException` |
| `Auth/Commands/ResetPassword/ResetPasswordHandler` | look up hash, `TryConsumeAsync`, `UpdatePasswordAsync` | `UserManager.ResetPasswordAsync(user, token, newPassword)`; a failed result becomes the single generic `ValidationException` on `token` |
| `Auth/Commands/RecoverUsername/RecoverUsernameHandler` | `IUserStore.FindActiveByEmailAsync` | `UserManager.FindByEmailAsync` + an `IsActive`/`DeletedAt` check; the enumeration-safe always-200 response is unchanged |
| `Users/Commands/CreateStaffUser/CreateStaffUserHandler` | build `User`, hash temp password, `AddAsync` | `UserManager.CreateAsync(user, tempPassword)` + `AddToRoleAsync(user, role)`; `OnboardingCompletedAt` stays null so the account is forced through onboarding; the welcome email stays best-effort with its log-and-continue swallow |
| `Users/Commands/ChangeUserRole/ChangeUserRoleHandler` | `IUserStore.ReplaceRolesAsync` | `UserManager.GetRolesAsync` → `RemoveFromRolesAsync` → `AddToRoleAsync`, in that order (remove first, so re-assigning a held role cannot trip the unique index) |
| `Users/Commands/ChangeOwnPassword/ChangeOwnPasswordHandler` | verify + `UpdatePasswordAsync` | `UserManager.ChangePasswordAsync(user, current, next)`; a `PasswordMismatch` result becomes the `currentPassword` `ValidationException` from Task 2 |
| `Users/Commands/UpdateOwnProfile/UpdateOwnProfileHandler` | `IUserStore.FindByIdAsync` + save | `UserManager.FindByIdAsync` + `UpdateAsync` |
| `Users/Queries/ListUsers/ListUsersHandler` | `IUserStore.ListAsync` | `IUserDirectory.ListAsync` + `RolesForAsync` |
| `Users/Queries/GetUser/GetUserHandler` | `IUserStore.FindByIdAsync` | `UserManager.FindByIdAsync` + `GetRolesAsync` |
| `Auth/RequestActorFactory` | `IUserStore.OnboardingStateAsync` | `IUserDirectory.OnboardingStateAsync` — unchanged behavior, renamed port |

`IPasswordHasher`, `BcryptPasswordHasher`, `IPasswordResetTokenStore`, `PasswordResetToken`,
`TokenDigests` and the `BCrypt.Net-Next` package reference are all deleted. Grep afterwards:
`grep -rn "BCrypt\|IPasswordHasher\|TokenDigests" src/` must return zero hits.

**`UserManager` returns `IdentityResult`, and the plan is explicit about the mapping** so no
handler invents one:

```csharp
// src/Hsm.Application/Auth/IdentityResultExtensions.cs
using FluentValidation;
using FluentValidation.Results;
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Identity;

namespace Hsm.Application.Auth;

/// <summary>
/// One translation from IdentityResult to the closed exception set, so every
/// handler refuses the same way. Duplicate keys are conflicts; everything else
/// Identity reports is a problem with what the caller sent.
/// </summary>
public static class IdentityResultExtensions
{
    public static void ThrowIfFailed(this IdentityResult result, string field)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e =>
                e.Code is "DuplicateUserName" or "DuplicateEmail" or "DuplicateRoleName"))
        {
            throw new ConflictException(
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        throw new ValidationException(
            result.Errors.Select(e => new ValidationFailure(field, e.Description)));
    }
}
```

- [ ] **Step 7: Make `HsmDbContext` an `IdentityDbContext` and map the tables**

Change the declaration to:

```csharp
public class HsmDbContext(DbContextOptions<HsmDbContext> options)
    : IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>(options)
```

`OnModelCreating` must call `base.OnModelCreating(modelBuilder)` **first** — Identity's
configuration has to be in place before it is overridden. Replace `ConfigureIdentity` with:

```csharp
    /// <summary>
    /// Identity's tables, mapped onto this repository's snake_case names, with
    /// the two properties the hospital schema adds to the default: citext
    /// login/email columns and unique indexes filtered on DeletedAt, so a
    /// soft-deleted account frees its username and email for reuse.
    /// </summary>
    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<HsmUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.UserName).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.NormalizedUserName).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.Email).HasColumnType("citext").HasMaxLength(256);
            user.Property(u => u.NormalizedEmail).HasColumnType("citext").HasMaxLength(256);

            // Identity declares these two indexes unique and unfiltered; the
            // filter is what makes soft delete free the name.
            user.HasIndex(u => u.NormalizedUserName)
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("ix_users_normalized_user_name");
            user.HasIndex(u => u.NormalizedEmail)
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("ix_users_normalized_email");
        });

        modelBuilder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasData(RoleCatalog.All.Select(name => new IdentityRole<Guid>
            {
                Id = RoleCatalog.IdFor(name),
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                // A fixed stamp, because a random one per model build would make
                // every `migrations add` produce a spurious update.
                ConcurrencyStamp = RoleCatalog.IdFor(name).ToString(),
            }));
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        // Integration accounts and their refresh tokens are NOT Identity types;
        // they keep their own tables (see IdentityStores).
        modelBuilder.Entity<IntegrationAccount>(account =>
        {
            account.ToTable("users_integration");
            account.Property(a => a.Name).HasColumnType("citext");
        });
        modelBuilder.Entity<IntegrationRefreshToken>()
            .ToTable("refresh_token_user_integration");
    }
```

`refresh_token_users` and `password_reset_tokens` are **dropped**: browser sessions are the
Identity cookie now (Task 12) and password reset tokens are Identity's
`AspNetUserTokens`/`user_tokens` (Task 13). `UserRefreshToken` and its store go with them.

Registration, `src/Hsm.Infrastructure/Identity/IdentityRegistration.cs`:

```csharp
using Hsm.Domain.Identity;
using Hsm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Identity's core services: UserManager, RoleManager, the token providers used
/// by password reset, and the EF stores. SignInManager and the authentication
/// SCHEMES are not here — they need HttpContext and belong to a host (Task 12).
/// </summary>
public static class IdentityRegistration
{
    public static IServiceCollection AddHsmIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<HsmUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Deliberately modest: length is the only requirement that
                // measurably helps, and character-class rules push users toward
                // predictable substitutions. Identity's default hasher (PBKDF2,
                // v3 format) is left alone.
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HsmDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<Application.Auth.IUserDirectory, UserDirectory>();
        return services;
    }
}
```

Call it from `AddHsmInfrastructure`.

- [ ] **Step 8: Regenerate the `Initial` migration from scratch**

```bash
rm -rf src/Hsm.Infrastructure/Migrations
dotnet tool restore
dotnet dotnet-ef migrations add Initial \
  --project src/Hsm.Infrastructure \
  --startup-project src/Hsm.Api \
  --output-dir Migrations
```

Then prove the migration equals the model, and that the drop-and-recreate works:

```bash
dotnet dotnet-ef database drop --force \
  --project src/Hsm.Infrastructure --startup-project src/Hsm.Api
dotnet dotnet-ef database update \
  --project src/Hsm.Infrastructure --startup-project src/Hsm.Api
dotnet dotnet-ef migrations has-pending-model-changes \
  --project src/Hsm.Infrastructure --startup-project src/Hsm.Api
```

Expected: `No changes have been made to the model since the last migration.`

Inspect the generated `Initial.cs` before committing and confirm it contains: the `citext`
extension, the two filtered unique indexes on `users`, the six Identity tables under their
snake_case names, 38 `InsertData` rows on `roles`, the `jsonb` columns on `patient`, and
`uq_patient_identifier_system_value`. If any is missing, the model configuration is wrong —
fix the model, delete `Migrations/`, and regenerate. Never hand-edit a scaffolded migration.

- [ ] **Step 9: Update the test fixtures**

`tests/Hsm.Api.Tests/Support/ApiFactory.cs` — `SeedUserAsync` builds an `HsmUser` through
`UserManager` instead of writing a row with a bcrypt hash:

```csharp
    public async Task<Guid> SeedUserAsync(
        string username,
        string password,
        string role,
        DateTimeOffset? onboardingCompletedAt,
        string? email = null,
        bool isActive = true)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HsmUser>>();
        var user = new HsmUser
        {
            UserName = username,
            Email = email ?? $"{username}@api.test",
            FirstName = "Api",
            FirstLastName = "Test",
            OnboardingCompletedAt = onboardingCompletedAt,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        var assigned = await users.AddToRoleAsync(user, role);
        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(e => e.Description)));
        return user.Id;
    }
```

`tests/Hsm.Integration.Tests/TestServices.cs` gets the same treatment wherever it constructs a
`User`.

- [ ] **Step 10: Run everything**

Run: `dotnet test Hsm.sln`
Expected: PASS. Every suite drops and re-migrates its own database, so the schema change is
absorbed by the fixtures with no manual step.

Then reset the developer database once, which this plan sanctions:

```bash
dotnet run --project src/Hsm.Api -- --migrate
```

- [ ] **Step 11: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "feat: ASP.NET Core Identity membership and a regenerated schema

HsmUser : IdentityUser<Guid> absorbs the profile columns; HsmDbContext becomes
an IdentityDbContext with the Identity tables mapped onto this repo's
snake_case names, citext logins, and unique indexes filtered on DeletedAt so a
soft delete frees the name. All 38 catalog roles are seeded by the migration
through deterministic ids, not by a startup task two hosts could race. bcrypt,
the SHA-256 pre-digest, the hand-rolled password-reset token table and our
IUserStore are deleted; what UserManager cannot do survives as IUserDirectory,
renamed so it cannot collide with Identity's own IUserStore. The Initial
migration is regenerated from scratch and dev databases are reset once."
```

---

### Task 12: Authentication middleware (Identity part 2)

**Files:**
- Create: `src/Hsm.Api/Identity/HsmActorMiddleware.cs`,
  `src/Hsm.Api/Identity/HsmAntiforgery.cs`,
  `src/Hsm.Infrastructure/Identity/HsmUserClaimsPrincipalFactory.cs`,
  `src/Hsm.Infrastructure/Identity/HsmAuthenticationSchemes.cs`
- Modify: `Directory.Packages.props` (+ `Microsoft.AspNetCore.Authentication.JwtBearer`),
  `src/Hsm.Api/Hsm.Api.csproj`, `src/Hsm.Web/Hsm.Web.csproj`,
  `src/Hsm.Api/Program.cs`, `src/Hsm.Web/Program.cs`,
  `src/Hsm.Infrastructure/Identity/IdentityRegistration.cs`,
  `src/Hsm.Application/Auth/RequestActorFactory.cs`,
  `src/Hsm.Api/Auth/HttpCurrentPrincipal.cs`,
  `src/Hsm.Web/Auth/{ShellActor,HsmAuthenticationStateProvider}.cs`,
  `tests/Hsm.Api.Tests/Support/ApiFactory.cs` (the seam's body)
- Delete: `src/Hsm.Api/Auth/{RequestAuth,CsrfProtection,AuthCookies}.cs`,
  `src/Hsm.Web/Auth/{HsmCookieAuthenticationHandler,AuthCookies,RequestAuth}.cs`,
  `src/Hsm.Contracts/Auth/AuthCookiePolicy.cs`
- Test: `tests/Hsm.Api.Tests/Identity/AuthenticationTests.cs`,
  `tests/Hsm.Api.Tests/Identity/AntiforgeryTests.cs`

**Interfaces:**
- Consumes: `HsmUser`, `AddHsmIdentity` (Task 11), `RequestActor`/`ICurrentPrincipal`.
- Produces:
  - `static class HsmAuthenticationSchemes { public const string Adaptive = "hsm-adaptive"; }`
  - `static class HsmClaims { public const string OnboardingCompletedAt = "hsm:onboarding_completed_at"; }`
  - `IServiceCollection AddHsmIdentityAuthentication(this IServiceCollection, IConfiguration)`
  - `IApplicationBuilder UseHsmActor(this IApplicationBuilder)`
  - `RequestActorFactory.CreateAsync(ClaimsPrincipal, CancellationToken)`

**What `AuthCookiePolicy` becomes.** It is **deleted**. It existed to keep two hand-rolled
cookie writers agreeing on names, paths, `SameSite` and max-ages. Those are now
`CookieAuthenticationOptions` on one scheme, configured once in
`AddHsmIdentityAuthentication` (Infrastructure) and shared by both hosts because both call it.
`Hsm.Contracts` returns to holding only `Ui/` and `PagedResult<T>` — and its purity test
(`ContractsPurityTests`) keeps passing because nothing was added, only removed.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/Hsm.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```

Pin at `10.0.10`.

- [ ] **Step 2: Write the failing authentication and antiforgery tests**

`tests/Hsm.Api.Tests/Identity/AuthenticationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class AuthenticationFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_authn";
}

public class AuthenticationTests(AuthenticationFactory factory)
    : IClassFixture<AuthenticationFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_session_cookie_is_http_only_strict_and_not_a_jwt()
    {
        var username = $"u{Guid.NewGuid():N}"[..20];
        await factory.SeedUserAsync(
            username, AuthenticationFactory.SeedPassword, Roles.Doctor, DateTimeOffset.UtcNow);
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/identity/login",
            new { username, password = AuthenticationFactory.SeedPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie").Where(c => c.StartsWith("hsm.session=", StringComparison.Ordinal)));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        // The session value is a protected blob, not a readable JWT.
        Assert.DoesNotContain("eyJ", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cookie_session_authenticates_a_subsequent_request()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_bearer_token_is_401_and_never_falls_back_to_a_cookie()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-token");

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 401);
    }

    [Fact]
    public async Task Roles_reach_the_pipeline_from_identity_role_claims()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse);

        var response = await client.GetAsync("/api/v1/users", CancellationToken.None);

        // Authenticated, wrong role — proves the role claim was read, not that
        // authentication failed.
        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task A_pending_onboarding_actor_is_refused_by_the_pipeline()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Nurse, onboarded: false);

        var response = await client.GetAsync("/api/v1/templates", CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }
}
```

`tests/Hsm.Api.Tests/Identity/AntiforgeryTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hsm.Domain.Identity;

namespace Hsm.Api.Tests.Identity;

public sealed class AntiforgeryFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_antiforgery";

    /// <summary>Antiforgery is skipped for the seam's own sign-in helper, so
    /// this suite drives the header by hand.</summary>
    public async Task<(HttpClient Client, string Token)> TokenClientAsync(string role)
    {
        var client = await AuthenticatedClientAsync(role);
        var response = await client.GetAsync("/api/v1/identity/csrf", CancellationToken.None);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);
        return (client, body.GetProperty("token").GetString()!);
    }
}

public class AntiforgeryTests(AntiforgeryFactory factory)
    : IClassFixture<AntiforgeryFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_cookie_authenticated_unsafe_request_without_the_header_is_403()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Doctor);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = AntiforgeryFactory.SeedPassword, newPassword = "New-Passw0rd" },
            CancellationToken.None);

        await ProblemAssert.ProblemAsync(response, 403);
    }

    [Fact]
    public async Task The_same_request_with_the_header_succeeds()
    {
        var (client, token) = await factory.TokenClientAsync(Roles.Doctor);
        using (client)
        {
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);

            var response = await client.PostAsJsonAsync(
                "/api/v1/users/me/password",
                new { currentPassword = AntiforgeryFactory.SeedPassword, newPassword = "New-Passw0rd" },
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task Safe_methods_never_need_the_header()
    {
        using var client = await factory.AuthenticatedClientAsync(Roles.Admin);

        var response = await client.GetAsync("/api/v1/users?pageSize=1", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_bearer_authenticated_unsafe_request_never_needs_the_header()
    {
        // A bearer caller cannot be CSRF'd: nothing is sent ambiently. Requiring
        // a token from integrations would be theatre that breaks them.
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-token");

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/password",
            new { currentPassword = "x", newPassword = "y" },
            CancellationToken.None);

        // 401 from the bearer handler — NOT 403 from antiforgery.
        await ProblemAssert.ProblemAsync(response, 401);
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/Hsm.Api.Tests --filter "AuthenticationTests|AntiforgeryTests"`
Expected: FAIL — `/api/v1/identity/login` and `/api/v1/identity/csrf` do not exist yet
(Task 13 adds them; **this task adds them as thin endpoints so the tests can pass**, and
Task 13 completes the rest of the identity surface around them).

- [ ] **Step 4: Register the schemes**

Add to `src/Hsm.Infrastructure/Identity/IdentityRegistration.cs`:

```csharp
/// <summary>The scheme a request is authenticated by, chosen per request.</summary>
public static class HsmAuthenticationSchemes
{
    /// <summary>Forwards to bearer when the caller sent one, to the cookie otherwise.</summary>
    public const string Adaptive = "hsm-adaptive";
}

public static class HsmClaims
{
    /// <summary>ISO-8601 onboarding completion, cached in the principal. The USER ROW
    /// remains authoritative — see RequestActorFactory for why this is a cache.</summary>
    public const string OnboardingCompletedAt = "hsm:onboarding_completed_at";
}
```

```csharp
    /// <summary>
    /// Two authentication handlers behind one adaptive scheme: browsers carry
    /// the Identity application cookie, integrations carry a JWT. The selector
    /// reproduces the cookie-then-bearer resolution the hand-rolled RequestAuth
    /// did, with framework middleware instead — and the ORDER matters: a caller
    /// who sent a bearer token gets the bearer handler's answer, so a stale
    /// cookie can never silently rescue a bad token.
    /// </summary>
    public static IServiceCollection AddHsmIdentityAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var cookieSecure = configuration.GetValue("Auth:CookieSecure", defaultValue: false);

        var authentication = services.AddAuthentication(HsmAuthenticationSchemes.Adaptive);

        authentication.AddPolicyScheme(
            HsmAuthenticationSchemes.Adaptive,
            displayName: "Cookie or bearer",
            options => options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString()
                    .StartsWith("Bearer ", StringComparison.Ordinal)
                        ? JwtBearerDefaults.AuthenticationScheme
                        : IdentityConstants.ApplicationScheme);

        authentication.AddIdentityCookies();

        authentication.AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(configuration["Auth:JwtAccessSecret"]!)),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                // The frozen verifier had no clock tolerance and neither does
                // this one: a five-minute default skew is five extra minutes of
                // life for a revoked integration token.
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.NameIdentifier,
            };
        });

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "hsm.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy =
                cookieSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddScoped<IUserClaimsPrincipalFactory<HsmUser>, HsmUserClaimsPrincipalFactory>();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "hsm.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy =
                cookieSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }
```

**Each host sets its own cookie events**, because a 401 and a redirect to `/login` are both
correct answers to the same situation on different doors. In `Hsm.Api`'s `Program.cs`:

```csharp
// The REST door answers with status codes. A redirect to an HTML login page is
// the wrong answer to an API call and produces a 200-with-HTML that clients
// misparse as success.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
```

`Hsm.Web` leaves the defaults (redirect to `/login`), which is what its
`RedirectToLoginTests` already expect.

`src/Hsm.Infrastructure/Identity/HsmUserClaimsPrincipalFactory.cs`:

```csharp
using System.Security.Claims;
using Hsm.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hsm.Infrastructure.Identity;

/// <summary>
/// Adds the onboarding timestamp to the principal so the common case — an
/// onboarded user — needs no database round trip per request. It is a CACHE:
/// RequestActorFactory falls back to the user row whenever the claim is absent
/// or empty, which is what makes a deleted or still-pending account fail closed
/// even while it holds a valid cookie.
/// </summary>
public sealed class HsmUserClaimsPrincipalFactory(
    UserManager<HsmUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<HsmUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(HsmUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        ArgumentNullException.ThrowIfNull(user);
        if (user.OnboardingCompletedAt is { } completedAt)
        {
            identity.AddClaim(new Claim(
                HsmClaims.OnboardingCompletedAt,
                completedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return identity;
    }
}
```

- [ ] **Step 5: Rewrite the actor path**

`src/Hsm.Application/Auth/RequestActorFactory.cs` gains a `ClaimsPrincipal` overload and keeps
the existing derivation verbatim:

```csharp
    /// <summary>
    /// Builds the actor from an authenticated principal. Null for an
    /// unauthenticated one — the pipeline answers that with 401, so a route
    /// that needed no authentication is unaffected and one that did fails
    /// closed.
    /// </summary>
    public async Task<RequestActor?> CreateAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return await CreateAsync(
            id, roles, principal.FindFirstValue(HsmClaims.OnboardingCompletedAt), ct);
    }
```

`src/Hsm.Api/Identity/HsmActorMiddleware.cs`:

```csharp
using Hsm.Application.Auth;

namespace Hsm.Api.Identity;

/// <summary>
/// Publishes the authenticated caller as the request's actor, once, for every
/// route. It replaces the per-endpoint RequestAuth.GateAsync call: an endpoint
/// can no longer forget to establish identity, because it never had to
/// remember. Failing to produce an actor is safe — the pipeline refuses any
/// non-anonymous request without one — so the only direction this can be wrong
/// in is "too strict".
/// </summary>
public static class HsmActorMiddleware
{
    internal const string ActorItem = "Hsm.RequestAuth.Actor";

    public static IApplicationBuilder UseHsmActor(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var factory = context.RequestServices.GetRequiredService<RequestActorFactory>();
                context.Items[ActorItem] =
                    await factory.CreateAsync(context.User, context.RequestAborted);
            }

            await next();
        });
    }
}
```

`HttpCurrentPrincipal` keeps its shape and reads `HsmActorMiddleware.ActorItem`. Delete
`RequestAuth.cs` entirely, and delete the transitional actor-installing block Task 5 added.

- [ ] **Step 6: Write the antiforgery middleware**

`src/Hsm.Api/Identity/HsmAntiforgery.cs`:

```csharp
using Hsm.Application.Errors;
using Microsoft.AspNetCore.Antiforgery;

namespace Hsm.Api.Identity;

/// <summary>
/// Standard ASP.NET antiforgery, applied where it actually protects something:
/// an unsafe method authenticated by an AMBIENT credential (the session
/// cookie). A bearer caller supplies its credential explicitly and cannot be
/// forged into by a third-party page, so demanding a token from integrations
/// would break them to prevent nothing.
///
/// <para>UseAntiforgery() alone does not cover this: it validates form posts
/// against endpoint metadata, and this API takes JSON. Validating explicitly
/// here is the wiring, not a reimplementation — IAntiforgery still mints,
/// stores and compares the tokens.</para>
/// </summary>
public static class HsmAntiforgery
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public static IApplicationBuilder UseHsmAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(async (context, next) =>
        {
            if (RequiresValidation(context))
            {
                try
                {
                    await context.RequestServices.GetRequiredService<IAntiforgery>()
                        .ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    throw new ForbiddenException("Invalid or missing antiforgery token.");
                }
            }

            await next();
        });
    }

    internal static bool RequiresValidation(HttpContext context) =>
        !SafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)
        && !context.Request.Headers.Authorization.ToString()
            .StartsWith("Bearer ", StringComparison.Ordinal)
        && context.Request.Cookies.ContainsKey("hsm.session");
}
```

`Program.cs` middleware order, final for this task:

```csharp
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); }
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseHsmActor();
app.UseHsmAntiforgery();
```

Antiforgery runs **after** authentication so it can tell a cookie caller from a bearer one,
and **inside** the exception handler so its refusal renders as a 403 problem like every other.
There is no `UseAuthorization()` call: this door has no endpoint-level authorization policies
by design, and adding the middleware would only invite one.

- [ ] **Step 7: Add the two endpoints these tests need**

In `src/Hsm.Api/Identity/IdentityEndpoints.cs` (created here, completed in Task 13):

```csharp
        identity.MapPost("/login", Login).WithSummary("Sign in and start a session.");
        identity.MapGet("/csrf", Csrf).WithSummary("Issue an antiforgery token.");
```

```csharp
    private static async Task<IResult> Login(
        LoginRequest request,
        IDispatcher dispatcher,
        SignInManager<HsmUser> signInManager,
        CancellationToken ct)
    {
        // The COMMAND verifies the credential (and owns lockout accounting);
        // the ENDPOINT writes the cookie. Credential checking is application
        // logic that a Blazor circuit needs too; writing a cookie is transport
        // and only this door does it.
        var user = await dispatcher.Send(
            new LoginCommand(request.Username, request.Password), ct);
        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Ok(MeResource.From(user, await signInManager.UserManager.GetRolesAsync(user)));
    }

    private static IResult Csrf(HttpContext context, IAntiforgery antiforgery)
    {
        // GetAndStoreTokens writes the cookie half and hands back the half the
        // caller must echo in X-XSRF-TOKEN.
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new AntiforgeryTokenResource(tokens.RequestToken!));
    }
```

`LoginCommand` changes shape here: `ICommand<TokenPair>` becomes `ICommand<HsmUser>`, because
there is no token pair for a browser any more.

- [ ] **Step 8: Retarget the auth seam**

In `tests/Hsm.Api.Tests/Support/ApiFactory.cs`, `AuthenticatedClientAsync` keeps its signature
and changes its body: POST `/api/v1/identity/login` instead of `/v1/auth/login`, and replay
the `hsm.session` cookie. **No module test file changes.** This is the whole reason the seam
exists; if a module suite needs editing here, the seam was not used and that is a bug in the
earlier task, not here.

- [ ] **Step 9: Rework the shell**

`src/Hsm.Web` deletes `HsmCookieAuthenticationHandler`, `AuthCookies` and `RequestAuth`, calls
`AddHsmIdentityAuthentication` plus `AddAuthentication`/`AddAuthorization` and
`app.UseAuthentication(); app.UseAuthorization();`, and `ShellActor` builds its
`RequestActor` from `AuthenticationStateProvider`'s `ClaimsPrincipal` through the same
`RequestActorFactory.CreateAsync(ClaimsPrincipal, …)` overload. `HsmAuthenticationStateProvider`
becomes a thin wrapper over `ServerAuthenticationStateProvider` — the circuit's principal is
now the framework's, so there is nothing left to parse. The Blazor login form keeps its
`EditForm`/`FormName` antiforgery; `app.UseAntiforgery()` stays in `Hsm.Web` unchanged.

- [ ] **Step 10: Run everything**

Run: `dotnet test Hsm.sln`
Expected: PASS, including the three re-homed shell suites — "sign in at the shell, be
recognised at the API" now holds because both hosts share one Identity cookie and one data
protection key ring rooted in the same application discriminator.

> If the shell suites fail with a cookie the API cannot decrypt, the two in-memory hosts have
> different data-protection key rings. Fix it in the factory by pointing both at one
> `PersistKeysToFileSystem` directory plus `SetApplicationName("hsm")` — **not** by weakening
> the cookie. In a real deployment the same requirement holds and is stated in Task 17's docs.

- [ ] **Step 11: Format and commit**

```bash
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "feat: Identity cookie and JWT bearer replace the hand-rolled auth

One adaptive scheme forwards to JwtBearer for integrations and to the Identity
application cookie for browsers; the cookie is encrypted, HttpOnly,
SameSite=Strict and sliding, which retires browser refresh-token rotation
entirely. CsrfProtection is replaced by standard antiforgery, validated on
unsafe cookie-authenticated methods only — a bearer caller cannot be forged
into. RequestAuth.GateAsync is gone: one middleware installs the actor from
HttpContext.User for every route, so an endpoint cannot forget. AuthCookiePolicy
dies with the hand-rolled writers it kept in agreement."
```

---

### Task 13: Identity endpoints (Identity part 3)

**Files:**
- Modify: `src/Hsm.Api/Identity/IdentityEndpoints.cs` (completed),
  `src/Hsm.Api/Program.cs`,
  `src/Hsm.Application/Auth/Commands/**` (see the table),
  `tests/Hsm.Tests/Architecture/RequestPolicyClosureTests.cs`,
  `tests/Hsm.Tests/Auth/AuthRequestPolicyTests.cs`
- Create: `src/Hsm.Api/Identity/IdentityResource.cs`,
  `tests/Hsm.Api.Tests/Identity/IdentityEndpointTests.cs`
- Delete: `src/Hsm.Api/Auth/AuthEndpoints.cs`, `src/Hsm.Api/Http/ApiEnvelope.cs`,
  `src/Hsm.Application/Auth/Commands/{GeneratePin,ValidatePin,RefreshTokens}/`,
  `src/Hsm.Domain/Identity/RefreshTokens.cs`'s `UserRefreshToken`,
  `src/Hsm.Application/Auth/IUserRefreshTokenStore.cs`

**Route map:**

| Old route | New route | Dispatches | Success | Policy |
|---|---|---|---|---|
| `POST /v1/auth/signup` | `POST /api/v1/identity/register` | `RegisterCommand` (renamed from `SignupCommand`) | 201 `MeResource` + session cookie | `[AllowAnonymousRequest]` |
| `POST /v1/auth/login` | `POST /api/v1/identity/login` | `LoginCommand` | 200 `MeResource` + session cookie | `[AllowAnonymousRequest]` |
| `GET /v1/auth/logout` | **`POST`** `/api/v1/identity/logout` | — (`SignInManager.SignOutAsync`) | **204** | authenticated, `[AllowPendingOnboarding]` |
| `GET /v1/auth/profile` | `GET /api/v1/identity/me` | `GetMeQuery` (new, wraps `UserManager.FindByIdAsync`) | 200 `MeResource` | authenticated, `[AllowPendingOnboarding]` |
| `POST /v1/auth/onboarding` | `POST /api/v1/identity/onboarding` | `CompleteOnboardingCommand` | 200 `MeResource` (session refreshed) | `[AllowPendingOnboarding]` |
| `GET /v1/auth/csrf` | `GET /api/v1/identity/csrf` | — | 200 `{ token }` | anonymous |
| `POST /v1/auth/password/forgot` | `POST /api/v1/identity/password/forgot` | `ForgotPasswordCommand` | **202** `{ message }` | `[AllowAnonymousRequest]`, `RecoveryRateLimitPolicy` |
| `POST /v1/auth/password/reset` | `POST /api/v1/identity/password/reset` | `ResetPasswordCommand` | **204** | `[AllowAnonymousRequest]`, `RecoveryRateLimitPolicy` |
| `POST /v1/auth/username/recover` | `POST /api/v1/identity/username/recover` | `RecoverUsernameCommand` | **202** `{ message }` | `[AllowAnonymousRequest]`, `RecoveryRateLimitPolicy` |
| `GET /v1/auth/refresh` | `POST /api/v1/identity/refresh` | integrations only — **Task 14** | — | — |
| `POST /v1/auth/signup/integration` | `POST /api/v1/identity/integrations/register` | **Task 14** | — | — |
| `POST /v1/auth/logout/integration` | `POST /api/v1/identity/integrations/logout` | **Task 14** | — | — |
| `POST /v1/auth/pin/generate` | **dropped** | — | — | frozen no-op stub |
| `POST /v1/auth/pin/validate` | **dropped** | — | — | frozen no-op stub |

**Carry the rate limit policy verbatim.** `RecoveryRateLimitPolicy` keeps its name
(`"auth-recovery"`), its fixed 10-per-60-seconds window, and its partition key
`$"{remoteIp}:{path}"`. The three routes keep `.RequireRateLimiting(AuthEndpoints.RecoveryRateLimitPolicy)`
— the constant moves to `IdentityEndpoints` with the file. The **per-account** limit inside
`ForgotPasswordHandler` (a separate, second limit that produces `TooManyRequestsException`)
also stays. Two limits, both preserved: one per IP at the edge, one per account in the handler.

**Enumeration-safety, restated so it is not lost in the rewrite:**
- `password/forgot` returns 202 with the same `{ message }` whether or not the email exists.
- `username/recover` returns 202 with the same `{ message }` in both cases.
- `password/reset` returns 204 on success and one identical `ValidationException` on `token`
  for an unknown token, an expired token and a consumed token.
- `login` returns one identical `UnauthorizedException` for an unknown username, a wrong
  password and a locked-out account.

Resource records (`src/Hsm.Api/Identity/IdentityResource.cs`):

```csharp
public sealed record MeResource(
    Guid Id, string Username, string Email, string FirstName, string? SecondName,
    string FirstLastName, string? SecondLastName, string? PhoneNumber,
    IReadOnlyList<string> Roles, DateTimeOffset? OnboardingCompletedAt);

public sealed record RegisterRequest(
    string Username, string Email, string Password, string FirstName, string FirstLastName,
    string? SecondName, string? SecondLastName, string? PhoneNumber, string? Gender);

public sealed record LoginRequest(string Username, string Password);

public sealed record OnboardingRequest(string NewPassword, string PhoneNumber, string ConfirmEmail);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record RecoverUsernameRequest(string Email);

public sealed record AntiforgeryTokenResource(string Token);

public sealed record AcknowledgedResource(string Message);
```

Command changes:

| Command | Was | Becomes |
|---|---|---|
| `SignupCommand` | `ICommand<TokenPair>` | `RegisterCommand : ICommand<HsmUser>` (folder renamed `Commands/Register/`) |
| `LoginCommand` | `ICommand<TokenPair>` | `ICommand<HsmUser>` |
| `LogoutCommand` | `ICommand<Unit>`, `[AllowAnonymousRequest]` | **deleted** — signing out is `SignInManager.SignOutAsync`, a transport act with no application state to change |
| `CompleteOnboardingCommand` | `ICommand<TokenPair>` | `ICommand<HsmUser>` |
| `RefreshTokensCommand` | browser refresh rotation | **deleted** — Task 14 introduces the integration-only replacement |
| `GeneratePinCommand`, `ValidatePinCommand` | frozen no-op stubs | **deleted** |
| — | — | `GetMeQuery : IQuery<HsmUser>`, authenticated, `[AllowPendingOnboarding]` (new) |

- [ ] **Step 1–7: TDD as in Task 5**

Write `tests/Hsm.Api.Tests/Identity/IdentityEndpointTests.cs` covering: register → 201 with a
session; login → 200; logout → 204 and the following request is 401; `me` → 200 for a pending
user (proving `[AllowPendingOnboarding]`); onboarding → 200 and `me` then reports a completed
timestamp; forgot/recover → 202 with the identical body for a known and an unknown email; reset
with a bad token → 400 on `token`; the three recovery routes → 429 after 10 calls in a window;
`/api/v1/auth/*` and `/v1/auth/*` → 404. Run, see it fail, implement, run again.

- [ ] **Step 8: Delete the last of the envelope**

```bash
git rm src/Hsm.Api/Http/ApiEnvelope.cs
rmdir src/Hsm.Api/Http
grep -rn "ApiEnvelope" src/ tests/
```

The grep must return **zero** hits. This is the moment the Task 2 scaffolding is paid off.

- [ ] **Step 9: Update the policy-closure snapshot**

`tests/Hsm.Tests/Architecture/RequestPolicyClosureTests.cs`'s `ExpectedPolicies` array is the
one place the whole request closure is pinned. Remove `GeneratePinCommand`,
`ValidatePinCommand`, `RefreshTokensCommand`, `LogoutCommand`, `ListEmailRecipientsQuery` and
`GetEmailRecipientQuery`; rename `SignupCommand` → `RegisterCommand` and
`ListEmailBatchesQuery` → `ListEmailsQuery`; add `GetMeQuery: AllowPendingOnboarding`. The
count falls from 53 to **48**; update the doc comment's number with it. Task 14 adds three
more and Task 17 confirms the final list.

Also update `tests/Hsm.Tests/Auth/AuthRequestPolicyTests.cs`, which names the deleted
commands directly.

- [ ] **Step 10: Run everything, format, commit**

```bash
dotnet test Hsm.sln && dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: /api/v1/identity replaces the /v1/auth surface

Register, login, logout, me, onboarding and the three recovery routes move to
resource paths with real status codes: 204 for logout and reset, 202 for the
two enumeration-safe recovery acknowledgements. Sign-in is SignInManager
writing the Identity cookie; the browser refresh-rotation command and the two
frozen PIN no-op stubs are deleted. Both recovery rate limits survive —
per-IP at the edge, per-account in the handler — and every enumeration-safe
response still answers identically for a known and an unknown account. The
last of ApiEnvelope goes with the last /v1 route."
```

---

### Task 14: Integration tokens (Identity part 4)

Integration accounts are the one caller that cannot hold a browser cookie. They keep JWTs —
but the refresh token stops being a bcrypt-hashed JWT and becomes what a refresh token should
be: an opaque high-entropy value, hashed at rest, rotated on use.

**Files:**
- Create: `src/Hsm.Application/Auth/Commands/RefreshIntegrationTokens/{Command,Handler}.cs`,
  `src/Hsm.Application/Auth/IntegrationTokenIssuer.cs` (from `TokenIssuer.cs`),
  `tests/Hsm.Api.Tests/Identity/IntegrationTokenTests.cs`
- Modify: `src/Hsm.Api/Identity/IdentityEndpoints.cs`,
  `src/Hsm.Application/Auth/Commands/{SignupIntegration→RegisterIntegration,LogoutIntegration,IssueIntegrationTokens,RevokeIntegrationTokens}/`,
  `src/Hsm.Infrastructure/Identity/{IdentityStores,JwtAuthTokenCodec→IntegrationTokenCodec}.cs`,
  `tests/Hsm.Tests/Architecture/RequestPolicyClosureTests.cs`
- Delete: `src/Hsm.Application/Auth/TokenIssuer.cs`

**Route map:**

| Route | Dispatches | Success | Policy |
|---|---|---|---|
| `POST /api/v1/identity/integrations/register` | `RegisterIntegrationCommand(name, description, functionality)` | 201 `IntegrationTokenResource` | `[RequireRole(Roles.Admin)]` |
| `POST /api/v1/identity/integrations/logout` | `LogoutIntegrationCommand(token)` | 204 | `[RequireRole(Roles.Admin)]` |
| `POST /api/v1/identity/refresh` | `RefreshIntegrationTokensCommand(rawRefreshToken)` | 200 `IntegrationTokenResource` | `[AllowAnonymousRequest]` — the refresh token IS the credential |

```csharp
public sealed record IntegrationTokenResource(
    string AccessToken, string RefreshToken, int ExpiresInSeconds);

public sealed record RegisterIntegrationRequest(
    string Name, string Description, string Functionality);

public sealed record RefreshRequest(string RefreshToken);
```

**The refresh token, exactly:**

```csharp
// src/Hsm.Application/Auth/IntegrationTokenIssuer.cs (the parts that change)

/// <summary>
/// 256 bits of entropy, URL-safe. It is opaque on purpose: a refresh token
/// carries no claims a reader could act on, so there is nothing to validate and
/// nothing to leak — its only property is that it matches a stored row.
/// </summary>
private static string NewRefreshToken() =>
    Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

/// <summary>
/// SHA-256, not bcrypt. bcrypt exists to make a LOW-entropy secret expensive to
/// guess; a 256-bit random value is not guessable, so the work factor buys
/// nothing and costs ~100 ms of held connection on every refresh. The frozen
/// stack's SHA-256 pre-digest existed only because bcrypt silently truncates
/// past 72 bytes — with bcrypt gone, so is the reason for the pre-digest.
/// </summary>
private static string HashRefreshToken(string token) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
```

**Rotation on use**, in `RefreshIntegrationTokensHandler`, inside the ambient transaction
`TransactionBehavior` already opens:

1. Hash the presented token and look up the active row by hash.
2. No row → `UnauthorizedException("Invalid refresh token.")`. (No distinction between "never
   existed" and "already used" — both are the same answer to a caller.)
3. Deactivate that row.
4. Insert a new row with the hash of a freshly minted token.
5. Issue a new access JWT and return both.

Because steps 3–4 share the command's transaction, two concurrent refreshes with the same
token cannot both succeed: the second finds no active row.

Access tokens keep their current JWT shape and `Auth:JwtAccessSecret` signing key, validated
by the `AddJwtBearer` handler Task 12 registered. `JwtAuthTokenCodec` is renamed
`IntegrationTokenCodec` and loses its refresh-token half — there is no refresh JWT any more.
`TokenIssuer`'s developer-role-outside-dev `ForbiddenException` moves across unchanged.

Integration accounts remain rows in `users_integration` with the `integration` role; the JWT's
role claim carries `integration`, which is what makes `[RequireRole]` work identically for
them.

Module tests (`tests/Hsm.Api.Tests/Identity/IntegrationTokenTests.cs`): an admin registers an
integration and receives both tokens; the access token authenticates a `/api/v1` call as a
bearer; a non-admin registering is 403; refresh returns a **different** refresh token; the old
refresh token is rejected afterwards; a garbage refresh token is 401; a browser cookie session
calling `/api/v1/identity/refresh` is 401 (it has no refresh token at all — proving the route
serves integrations only).

Policy-closure additions: `RegisterIntegrationCommand: RequireRole(admin)`,
`RefreshIntegrationTokensCommand: AllowAnonymousRequest`; `SignupIntegrationCommand` removed.
Count goes 48 → 49.

Commit:

```bash
git add -A && git commit -m "feat: opaque, rotating refresh tokens for integrations

Integrations keep JWT access tokens, validated by AddJwtBearer. The refresh
token stops being a bcrypt-hashed JWT and becomes 256 bits of opaque entropy
SHA-256'd at rest and rotated on every use inside the command's transaction,
so two concurrent refreshes cannot both win. bcrypt bought nothing against a
value that is not guessable, and its SHA-256 pre-digest existed only to work
around bcrypt's 72-byte truncation. POST /api/v1/identity/refresh serves
integrations and nothing else; browsers have a sliding cookie instead."
```

---

### Task 15: Renames and debt retirement

Four pieces of debt the freeze forced us to carry, retired together because each is a
mechanical sweep and splitting them into four commits buys nothing.

**Files:**
- Move: `src/Hsm.Infrastructure/Jobs/` → `src/Hsm.Infrastructure/Queue/` (8 files),
  `src/Hsm.Application/Auth/` → `src/Hsm.Application/Identity/`,
  `src/Hsm.Api/Auth/` → `src/Hsm.Api/Identity/` (whatever survives Task 12)
- Modify: `src/Hsm.Application/Abstractions/IUnitOfWork.cs`,
  `src/Hsm.Infrastructure/Persistence/EfUnitOfWork.cs`,
  `src/Hsm.Infrastructure/DependencyInjection.cs`, 21 handlers,
  `src/Hsm.Worker/JobConsumerService.cs`,
  `tests/Hsm.Api.Tests/Support/ApiFactory.cs`,
  `tests/Hsm.Integration.Tests/{WorkerEndToEndTests,ConsumerLeaseTests}.cs`
- Delete: `src/Hsm.Application/Auth/IAuthUnitOfWork.cs`,
  `src/Hsm.Infrastructure/Identity/IdentityStores.cs`'s `AuthUnitOfWork`

**Interfaces:**
- Produces: `IUnitOfWork` gains `Task SaveChangesAsync(CancellationToken ct = default)`.

- [ ] **Step 1: `Infrastructure/Jobs` → `Infrastructure/Queue`**

```bash
git mv src/Hsm.Infrastructure/Jobs src/Hsm.Infrastructure/Queue
grep -rl "Hsm.Infrastructure.Jobs" src/ tests/ | xargs sed -i 's/Hsm\.Infrastructure\.Jobs/Hsm.Infrastructure.Queue/g'
```

Type names do **not** change: a queue holds jobs, so `JobQueueTopology`, `JobEnvelope`,
`RedisStreamJobQueue`, `JobNameRegistry`, `DelayedJobPump`, `JobConsumerService` and
`[JobName]` all keep their names. Only the folder and namespace move. Redis key shapes
(`{prefix}:jobs:{queue}`) do **not** change either — renaming them would strand every job
already in a dev Redis for no benefit.

- [ ] **Step 2: `Jobs:*` → `Queue:*` config keys — the complete inventory**

Verified: **no `appsettings*.json`, no `docker/docker-compose.yaml`, no `secrets.env` or
`secrets.env.template` contains a `Jobs` key.** Every value runs on its code default. The
sweep is source and tests only, and these are all of them:

| Key | Read at | New key |
|---|---|---|
| `Jobs:KeyPrefix` | `Infrastructure/Queue/JobQueueRegistration.cs:55` (+ doc comments at :43 and `JobQueueTopology.cs:67`) | `Queue:KeyPrefix` |
| `Jobs:LeaseTtlMs` | `JobQueueRegistration.cs:61` | `Queue:LeaseTtlMs` |
| `Jobs:UnknownJobRetryDelayMs` | `JobQueueRegistration.cs:65` | `Queue:UnknownJobRetryDelayMs` |
| `Jobs:UnknownJobMaxAttempts` | `JobQueueRegistration.cs:66` | `Queue:UnknownJobMaxAttempts` |
| `Jobs:PollIntervalMs` | `JobQueueRegistration.cs:68` | `Queue:PollIntervalMs` |
| `Jobs:DelayedPumpIntervalMs` | `JobQueueRegistration.cs:69` | `Queue:DelayedPumpIntervalMs` |
| `Jobs:StreamMaxLength` | `JobQueueRegistration.cs:70` | `Queue:StreamMaxLength` |

Non-code occurrences of the same strings that must move with them:

| Location | What it is |
|---|---|
| `src/Hsm.Worker/JobConsumerService.cs:80` | the runtime error text `"Jobs:LeaseTtlMs above it, or lower the queue's ClaimMinIdle."` |
| `tests/Hsm.Integration.Tests/ConsumerLeaseTests.cs:129` | `Assert.Contains("Jobs:LeaseTtlMs", …)` — pins the string above |
| `tests/Hsm.Api.Tests/Support/ApiFactory.cs` | `builder.UseSetting("Jobs:KeyPrefix", JobKeyPrefix)` |
| `tests/Hsm.Integration.Tests/WorkerEndToEndTests.cs:185` | `["Jobs:KeyPrefix"] = …` |

The sibling per-queue keys `Coms:*` and `Docs:*` are **not** under `Jobs:` and do not change.

Afterwards: `grep -rn '"Jobs:' src/ tests/` must return zero hits.

- [ ] **Step 3: `Application/Auth` and `Api/Auth` → `Identity`**

```bash
git mv src/Hsm.Application/Auth src/Hsm.Application/Identity
git mv src/Hsm.Api/Auth src/Hsm.Api/Identity   # merges into the folder Task 12 created
grep -rl "Hsm.Application.Auth\|Hsm.Api.Auth" src/ tests/ \
  | xargs sed -i 's/Hsm\.Application\.Auth/Hsm.Application.Identity/g; s/Hsm\.Api\.Auth/Hsm.Api.Identity/g'
```

Task 12 already created `src/Hsm.Api/Identity/` and moved the surviving files into it, so this
step reconciles: if `git mv` reports the target exists, move the remaining files individually
and remove the empty `Auth` directory. `src/Hsm.Application/Identity/` now holds
`IUserDirectory`, `IIntegrationAccountStore`, `IIntegrationRefreshTokenStore`,
`IRecoveryEmailer`, `IEnvironmentPolicy`, `IAuthTokenCodec`, `IdentityResultExtensions`,
`RequestActorFactory`, `IntegrationTokenIssuer`, and the `Commands`/`Queries` folders.

- [ ] **Step 4: Retire `IAuthUnitOfWork`**

Add the flush to `src/Hsm.Application/Abstractions/IUnitOfWork.cs`:

```csharp
namespace Hsm.Application.Abstractions;

/// <summary>
/// The transaction boundary and the flush, in one interface.
///
/// <para>There used to be two: IAuthUnitOfWork, historically named, was the one
/// 21 handlers across Users, Templates, Coms, Settings and Identity actually
/// used, while IUnitOfWork had exactly one consumer — TransactionBehavior.
/// Two names for one HsmDbContext is a standing invitation to believe they are
/// two boundaries. They never were.</para>
///
/// <para>The join rule is unchanged: the implementation JOINS an already-open
/// ambient transaction rather than nesting one. A caller that needs an
/// independent transaction — an audit row or a compensating action that must
/// survive an outer rollback — must not go through here; it will be silently
/// absorbed.</para>
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);

    /// <summary>Persists staged changes without opening a boundary of its own.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

Note the `ct` on `ExecuteInTransactionAsync` stays **required** while `SaveChangesAsync`'s is
optional — that matches the 21 call sites' existing `IAuthUnitOfWork.SaveChangesAsync(ct)`
usage and avoids touching call sites that pass it positionally.

`EfUnitOfWork` gains `public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);`
carrying over `AuthUnitOfWork`'s ambient-join logging verbatim. Then:

```bash
grep -rl "IAuthUnitOfWork" src/ | xargs sed -i 's/IAuthUnitOfWork/IUnitOfWork/g'
git rm src/Hsm.Application/Identity/IAuthUnitOfWork.cs
```

Delete `AuthUnitOfWork` from `IdentityStores.cs` and the
`services.AddScoped<IAuthUnitOfWork, AuthUnitOfWork>();` line from
`DependencyInjection.cs:305`. The 21 constructor parameters change type only — no call site's
arguments change, because both interfaces had the same `SaveChangesAsync` signature.

The 21 handlers, for the record: `Identity/IntegrationTokenIssuer`,
`Identity/Commands/{CompleteOnboarding,ForgotPassword,LogoutIntegration,ResetPassword,Register,RegisterIntegration}`,
`Users/Commands/{ChangeUserRole,CreateStaffUser,UpdateOwnProfile}`,
`Settings/Commands/UpdateSettings`, `Templates/TemplateParser`,
`Templates/Commands/{CreateTemplate,UpdateTemplate,DeleteTemplate}`,
`Coms/Commands/{DispatchEmailBatch,ResendEmailBatch,SendEmail,ProcessWebhookEvent,ReceiveWebhook}`.

- [ ] **Step 5: Delete every "frozen X" doc comment**

The word appears **484 times across 172 files** in `src/`. It is not a mechanical
find-and-replace: each comment either (a) says "this reproduces the frozen system", which is
now false and goes, or (b) explains *why* a behavior is the way it is, in which case the
explanation stays and only the frozen framing goes.

Work file by file, largest first, with `grep -rn "frozen\|Frozen" src/ --include=*.cs`:

| Comments to DELETE outright | Comments to KEEP, reworded |
|---|---|
| "Mirrors the frozen X" / "the frozen contract asserts" / "byte-identical to the frozen contract" | Why coms is strictly serial (a resend can overtake a retry) |
| "frozen at packages/common/src/enums/…" source pointers | Why `ReplaceRolesAsync` deletes before inserting (unique index) |
| "the frozen runtime returned 201 here" | Why the temp-password hash is computed before any write |
| "the frozen verifier had no clock tolerance" *(the behavior survives — say so without the frozen)* → "No clock skew: a five-minute default is five extra minutes of life for a revoked token." | Why `onboardingCompletedAt` is rebuilt per request rather than cached |
| "@AllowPending", "@Throttle long", "ValidationPipe", "response.interceptor.ts" and every other NestJS symbol name | Why `[NoAmbientTransaction]` exists for exactly two commands |
| "the frozen stub, verbatim" *(the stubs are deleted by Tasks 6/7/13, so these files are gone)* | Why the docs queue's `ClaimMinIdle` is ten minutes |

Verify: `grep -rni "frozen" src/ --include=*.cs` returns zero. References to the frozen tag
survive **only** in `docs/`, where they are historical record.

- [ ] **Step 6: Delete what the sweep emptied**

```bash
grep -rn "ApiEnvelope\|ApiException\|ApiErrorCode\|BodyValidator\|QueryValidator\|RouteParams\|AuthCookiePolicy\|IPasswordHasher\|BCrypt\|TokenDigests\|IAuthUnitOfWork\|IUserStore\b" src/ tests/
```

Every one of these must return zero hits in `src/`. Remove the now-unused
`BCrypt.Net-Next` `PackageVersion` from `Directory.Packages.props` and any empty directory
(`src/Hsm.Api/Http/`, `src/Hsm.Contracts/Auth/`).

- [ ] **Step 7: Run everything, format, commit**

```bash
dotnet test Hsm.sln && dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "refactor: retire the names and comments the freeze forced

Infrastructure/Jobs becomes Infrastructure/Queue (namespace and the seven
Queue:* configuration keys; type names and Redis key shapes are unchanged so
nothing in a running Redis is stranded). Application/Auth and Api/Auth become
Identity. IAuthUnitOfWork — the historically-named interface 21 handlers
actually used, next to an IUnitOfWork with one consumer — is retired into
IUnitOfWork, which gains SaveChangesAsync. And 484 'frozen X' doc comments
across 172 files are gone: the ones that said 'this reproduces NestJS' are
false now, and the ones that explained WHY a behavior exists kept the
explanation and lost the framing."
```

---

### Task 16: OpenAPI, Scalar, and the living spec

**Files:**
- Create: `src/Hsm.Api/OpenApi/OpenApiRegistration.cs`, `docs/reference/openapi.json`,
  `tests/Hsm.Api.Tests/OpenApi/OpenApiSpecTests.cs`
- Modify: `Directory.Packages.props`, `src/Hsm.Api/Hsm.Api.csproj`,
  `src/Hsm.Api/Program.cs`, every `*Endpoints.cs` (metadata sweep),
  `src/Hsm.Api/appsettings.Development.json`, `CLAUDE.md`

**Interfaces:**
- Produces:
  - `IServiceCollection AddHsmOpenApi(this IServiceCollection, IHostEnvironment, IConfiguration)`
  - `IEndpointRouteBuilder MapHsmOpenApi(this WebApplication)`
  - `GET /api/openapi.json` — the generated document
  - `GET /api` — Scalar

- [ ] **Step 1: Add the packages**

```bash
dotnet add src/Hsm.Api package Microsoft.AspNetCore.OpenApi
dotnet add src/Hsm.Api package Scalar.AspNetCore
```

Pin `Microsoft.AspNetCore.OpenApi` at `10.0.10` to match the framework packages; take whatever
`Scalar.AspNetCore` resolves and leave it pinned in `Directory.Packages.props`.

- [ ] **Step 2: Write the failing gate test**

`tests/Hsm.Api.Tests/OpenApi/OpenApiSpecTests.cs`:

```csharp
using System.Text.Json;

namespace Hsm.Api.Tests.OpenApi;

public sealed class OpenApiFactory : ApiFactory
{
    protected override string DatabaseName => "hsm_api_tests_openapi";
}

/// <summary>
/// The committed spec is an ARTIFACT of the surface, and this test is what
/// keeps it one. A route added, renamed, or given a different response type
/// fails here until the author regenerates the file — which makes every wire
/// change a visible line in a diff instead of something a client discovers.
/// </summary>
public class OpenApiSpecTests(OpenApiFactory factory) : IClassFixture<OpenApiFactory>, IAsyncLifetime
{
    /// <summary>Set HSM_OPENAPI_UPDATE=1 to rewrite the committed spec.</summary>
    private const string UpdateVariable = "HSM_OPENAPI_UPDATE";

    public Task InitializeAsync() => factory.EnsureSchemaAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_generated_document_matches_the_committed_one()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/openapi.json", CancellationToken.None);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var generated = Normalize(
            await response.Content.ReadAsStringAsync(CancellationToken.None));

        var path = CommittedSpecPath();
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, generated, CancellationToken.None);
            Assert.Fail($"Rewrote {path}. Re-run without {UpdateVariable} and commit the diff.");
        }

        var committed = Normalize(await File.ReadAllTextAsync(path, CancellationToken.None));
        Assert.True(
            string.Equals(generated, committed, StringComparison.Ordinal),
            $"The API surface changed. Regenerate with:\n" +
            $"  {UpdateVariable}=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests\n" +
            $"then review and commit {path}.");
    }

    [Fact]
    public async Task Scalar_is_served_at_the_api_root()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api", CancellationToken.None);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Re-serializes indented so the committed file is diff-readable and
    /// a formatting difference is never mistaken for a surface change.</summary>
    private static string Normalize(string json) =>
        JsonSerializer.Serialize(
            JsonDocument.Parse(json).RootElement,
            new JsonSerializerOptions { WriteIndented = true });

    private static string CommittedSpecPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Hsm.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Hsm.sln not found."),
            "docs", "reference", "openapi.json");
    }
}
```

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests`
Expected: FAIL — `/api/openapi.json` 404s.

- [ ] **Step 4: Write the registration**

`src/Hsm.Api/OpenApi/OpenApiRegistration.cs`:

```csharp
using Scalar.AspNetCore;

namespace Hsm.Api.OpenApi;

/// <summary>
/// The generated document and the reference UI, both gated by
/// <c>OpenApi:Enabled</c> — on in Development, off by default everywhere else.
/// A public schema of every route is a reconnaissance gift, and turning it on
/// in production should be a decision somebody makes in configuration rather
/// than a default nobody noticed.
/// </summary>
public static class OpenApiRegistration
{
    public const string DocumentName = "v1";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue("OpenApi:Enabled", defaultValue: environment.IsDevelopment());

    public static IServiceCollection AddHsmOpenApi(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (!IsEnabled(environment, configuration))
        {
            return services;
        }

        return services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "HSM API";
                document.Info.Version = "v1";
                // The servers list is whatever host generated the document,
                // which would make the committed artifact differ between a
                // developer's machine, CI, and the test harness. The spec
                // describes the SURFACE; the base URL is the deployment's.
                document.Servers?.Clear();
                return Task.CompletedTask;
            });
        });
    }

    public static void MapHsmOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!IsEnabled(app.Environment, app.Configuration))
        {
            return;
        }

        app.MapOpenApi("/api/openapi.json");
        app.MapScalarApiReference("/api", options => options
            .WithTitle("HSM API")
            .WithOpenApiRoutePattern("/api/openapi.json"));
    }
}
```

In `Program.cs`: `builder.Services.AddHsmOpenApi(builder.Environment, builder.Configuration);`
and `app.MapHsmOpenApi();` after the module maps. Add `"OpenApi": { "Enabled": true }` to
`src/Hsm.Api/appsettings.Development.json`.

- [ ] **Step 5: Sweep endpoint metadata**

Every route registered in Tasks 5–14 already carries `.WithTags(...)` on its group and
`.WithSummary(...)`/`.Produces<T>(...)`. Complete the sweep here so the document is useful:

| Group | Tag | Every route additionally declares |
|---|---|---|
| `/api/v1/identity` | `Identity` | `.ProducesProblem(401)` where authenticated; `.ProducesValidationProblem()` where it takes a body; `.ProducesProblem(429)` on the three recovery routes |
| `/api/v1/users` | `Users` | `.ProducesProblem(401).ProducesProblem(403)`; `.ProducesProblem(404)` on `{id}` routes; `.ProducesValidationProblem()` on bodies |
| `/api/v1/emails` | `Emails` | `.ProducesProblem(401)`; `.ProducesProblem(404)` on `{id}` routes |
| `/api/v1/webhooks` | `Webhooks` | `.ProducesProblem(401)` (bad signature) |
| `/api/v1/documents` | `Documents` | `.ProducesProblem(401).ProducesProblem(404)`; `.Accepts<IFormFile>("multipart/form-data")` on upload |
| `/api/v1/templates` | `Templates` | `.ProducesProblem(401).ProducesProblem(404).ProducesProblem(409)` on delete |
| `/api/v1/settings` | `Settings` | `.ProducesProblem(401).ProducesProblem(403)` |
| `/api/v1/system` | `System` | none — anonymous, always 200 |
| `/fhir/R4/Patient` | `FHIR` | `.ExcludeFromDescription()` — FHIR has its own published specification and describing it in our OpenAPI document is a second, weaker source of truth |

- [ ] **Step 6: Generate and commit the spec**

```bash
HSM_OPENAPI_UPDATE=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests
dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests
```

The first run writes `docs/reference/openapi.json` and fails on purpose; the second passes.
**Read the generated file before committing it** — it is the document a client integrator
reads, and a missing summary or a wrong `Produces<T>` is visible there and nowhere else.

- [ ] **Step 7: Add the regeneration command to `CLAUDE.md`**

Under the commands block:

```bash
# Regenerate the committed OpenAPI spec after any surface change
HSM_OPENAPI_UPDATE=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests
```

- [ ] **Step 8: Run everything, format, commit**

```bash
dotnet test Hsm.sln && dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "feat: generated OpenAPI, Scalar at /api, spec committed as an artifact

AddOpenApi plus per-route summaries, tags and Produces types; Scalar serves the
reference at /api and the document at /api/openapi.json, both behind
OpenApi:Enabled (Development on, production off by default — a full schema of
every route is a reconnaissance gift). The document is committed at
docs/reference/openapi.json and a test fails when it drifts, so every wire
change is a reviewable line in a diff. FHIR is excluded: it has a published
specification and a second, weaker copy of it helps nobody."
```

---

### Task 17: Docs and closure

**Files:**
- Modify: `docs/reference/dotnet-conventions.md`, `docs/ARCHITECTURE-DECISIONS.md`,
  `CLAUDE.md`, `tests/Hsm.Tests/Architecture/RequestPolicyClosureTests.cs`

- [ ] **Step 1: Rewrite the conventions document's stale sections**

| Section | Change |
|---|---|
| Header | Delete the sentence naming the frozen contract as the governing behavior; the OpenAPI document at `docs/reference/openapi.json` governs the wire now |
| "Where each kind of code goes" | `Validator` row → `AbstractValidator<TRequest>` (FluentValidation) beside the request record; `Adapter` example `Jobs/RedisStreamJobConsumer.cs` → `Queue/RedisStreamJobConsumer.cs`; `Contract test` row → **delete**; add a `Resource record` row (`Hsm.Api`, `{Module}/{Module}Resource.cs`); add an `API surface test` row (`Hsm.Api.Tests`, `{Module}/`) |
| Dependency arrows | Note that `Hsm.Domain` has no project references and exactly one framework package (`Microsoft.Extensions.Identity.Stores`), and why; note `Hsm.Contracts` now also holds `PagedResult<T>` |
| "Validator rule" | **Replace entirely.** The old rule was about matching the frozen envelope's refusal shape. The new rule: a request-shape or field rule is an `AbstractValidator`; a rule that needs the database or the resource's current state is a `ConflictException`/`NotFoundException` in the handler; there is no third option, because there is no general 400 |
| "Exact-403-message lesson" | **Delete.** The messages it protected are gone |
| "Slice results are domain entities, not DTOs" | Keep, and extend: the endpoint projects into a `*Resource` record which is an **allow-list**, so a new entity column cannot leak by being forgotten |
| "Ambient-transaction join rule" | Keep the join rule; delete the `IAuthUnitOfWork` known-debt paragraph — it is retired |
| "Accepted contract drift from removing the edge gates" | **Delete.** There is no frozen contract to drift from |
| "Two more documented deltas from the frozen system" | **Delete**, and replace the CSRF half with: the REST door validates standard antiforgery on unsafe cookie-authenticated methods; the Blazor shell uses `EditForm`/`FormName` + `UseAntiforgery()`; they are two mechanisms because they protect two different things |
| — | **New rule:** unknown JSON fields and unknown query parameters are ignored, not refused. `forbidNonWhitelisted` was NestJS reproduction; System.Text.Json's default is the standard behavior |
| — | **New rule:** paging is `page`/`pageSize`, defaults 1/20, cap 100, **refused** above the cap rather than clamped, via `PagingRules` |
| — | **New rule:** both hosts must share one data-protection key ring and one `SetApplicationName` for a session issued at either door to be honoured at the other |
| — | **New rule:** `/health` is liveness-only and probes nothing; dependency state is `GET /api/v1/system/status` |
| — | **New rule:** the committed `docs/reference/openapi.json` is regenerated with `HSM_OPENAPI_UPDATE=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests` and reviewed, never hand-edited |

- [ ] **Step 2: Amend `docs/ARCHITECTURE-DECISIONS.md`**

- §5.4 ("Three API contracts"): the internal/public split is one `/api/v1` surface plus the
  FHIR façade; note the OpenAPI artifact is the contract now.
- §9 ("Sequencing — freeze and rebuild"): record that the freeze **ended** on 2026-08-03 and
  the frozen contract is retired; the tag remains a historical reference.
- Decision log — append:

| # | Decision | Rationale | Status |
|---|---|---|---|
| 30 | Un-freeze the wire: `/api/v1` resources, plain JSON, RFC 9457 problems | The frozen NestJS envelope existed only to reproduce a system nobody runs any more; the project is greenfield as of 2026-08-03 and clients adapt afterwards | ✅ Executed |
| 31 | One `IExceptionHandler` over a closed exception set, no general 400 | A status-code catch-all lets any handler raise any status; five named exceptions make the mapping total and reviewable, and a refusal that is not about request shape is a Conflict or a bug | ✅ Executed |
| 32 | FluentValidation in the pipeline as the only validation system | Two systems (a hand-rolled pipeline validator plus a 776-line edge `ValidationPipe` clone) meant HTTP and in-process dispatch could disagree; one assembly-scanned validator per request cannot | ✅ Executed |
| 33 | Full ASP.NET Core Identity, PBKDF2, cookie + JWT bearer | Custom stores, bcrypt, JWT-in-cookie and hand-rolled CSRF were four maintained-by-us mechanisms with framework equivalents; prefer the maintained library | ✅ Executed |
| 34 | Opaque, SHA-256-hashed, rotate-on-use integration refresh tokens | bcrypt protects low-entropy secrets; a 256-bit random value is not guessable, so the work factor bought nothing and its 72-byte truncation was the only reason for the SHA-256 pre-digest | ✅ Executed |
| 35 | Generated OpenAPI committed as an artifact with a drift test | An unpublished API surface rots; a committed spec makes every wire change a reviewable diff line and gives integrators one source of truth | ✅ Executed |

- [ ] **Step 3: Update `CLAUDE.md`**

Remove the "Frozen API contract" paragraph and both deleted file references; point at
`docs/reference/openapi.json`; list the four test projects (`Hsm.Tests`, `Hsm.Api.Tests`,
`Hsm.Integration.Tests`, `Hsm.Web.Tests`); add the spec regeneration command; note that
`/api` serves Scalar in Development.

- [ ] **Step 4: Confirm the policy-closure snapshot's final state**

Run: `dotnet test tests/Hsm.Tests --filter RequestPolicyClosureTests`
Expected: PASS with 49 entries. If it fails, the diff *is* the list of request types whose
policy changed across this branch — read it, and only then edit the array.

- [ ] **Step 5: Verify every path referenced by the docs exists**

```bash
grep -ohrE '(docs|src|tests)/[A-Za-z0-9_./-]+' \
  docs/reference/dotnet-conventions.md docs/ARCHITECTURE-DECISIONS.md CLAUDE.md \
  | sort -u | while read -r p; do [ -e "$p" ] || echo "MISSING: $p"; done
```

Expected: no output.

- [ ] **Step 6: Full suite, format, final commit**

```bash
dotnet build Hsm.sln
dotnet test Hsm.sln
dotnet format Hsm.sln --verify-no-changes
git add -A && git commit -m "docs: conventions and architecture record the un-frozen surface

The conventions doc stops naming the frozen contract as governing behavior and
gains the rules this branch established: where a rule lives now that there is
no general 400, that resources are allow-lists, that unknown fields are
ignored rather than refused, that paging refuses rather than clamps, that both
hosts must share a data-protection key ring, and that /health is liveness
only. The drift entries the freeze produced — the message-less 403, the
validation-before-authorization ordering, the two 404 shapes — are deleted
along with the contract they drifted from. Six decision-log rows record the
un-freeze, the exception model, FluentValidation, Identity, the opaque refresh
token, and the committed spec."
```

---

## Self-Review

**Spec coverage.** Every requirement is claimed:

| Requirement | Task(s) |
|---|---|
| R1 `/api/v1`, FHIR at `/fhir/R4` | 5, 6, 7, 8, 9, 10, 13, 14 |
| R2 plain resource JSON, real status codes | 5–10, 13, 14 |
| R3 plural nouns, actions explicit, camelCase | 5–10 |
| R4 `PagedResult<T>` + `page`/`pageSize` | 4, 5, 6, 7, 9 |
| R5 one `IExceptionHandler`, problem+json, `traceId` | 2 |
| R6 closed exception set, `ApiException` deleted | 2 |
| R7 FHIR renders the same set as `OperationOutcome` | 2 (Step 7) |
| R8 rate limiter → 429 problem | 2 (Step 5) |
| R9 FluentValidation only; hand-rolled validators deleted | 3 |
| R10 `ValidationProblemDetails`, camelCase keys | 2 (Step 4), 3 (Step 8) |
| R11 Identity membership, PBKDF2, bcrypt deleted | 11 |
| R12 every catalog role seeded | 11 (Step 7) |
| R13 cookie for browsers, JWT bearer for integrations | 12 |
| R14 standard antiforgery + `/identity/csrf` | 12 |
| R15 Identity token providers, enumeration-safe, rate limits kept | 13 |
| R16 opaque rotating refresh tokens, integrations only | 14 |
| R17 regenerated `Initial`, no pending model changes | 11 (Step 8) |
| R18 `Jobs` → `Queue` incl. config keys | 15 (Steps 1–2) |
| R19 `Auth` → `Identity` | 15 (Step 3) |
| R20 `IAuthUnitOfWork` retired | 15 (Step 4) |
| R21 "frozen X" comments deleted | 15 (Step 5) |
| R22 OpenAPI + Scalar, config-gated | 16 |
| R23 committed spec + drift test | 16 |
| R24 `Hsm.Api.Tests` pins the surface | 1, and every task 2–16 adds to it |

Design-document items not in the R-list are also placed: dropped routes (`/coms/send/sms`,
`/coms/emails/recipients*`, `/docs/create`, `/auth/pin/*`) in Tasks 6, 7, 13; the "batch"
→ "email" rename in Task 6; the PATCH rule recorded in Task 7; the shell-factory topology
question, listed as open in the design, resolved in Task 1.

**Placeholder scan.** No `TBD`, no `TODO`, no "similar to Task N" standing in for content.
Every module task carries a complete route table and complete resource records rather than
prose. The three places that could have been placeholders are not:

- Task 2's sweep is a 53-row table naming every call site by file and line, including the
  eleven reclassifications where the status code changes and why.
- Task 3's rule port is a 19-row table naming each surviving rule, its new validator and its
  FluentValidation expression.
- Task 15's config sweep names all seven `Jobs:*` keys, the four non-code occurrences of the
  same strings, and states the verified fact that no configuration file contains any of them —
  so the implementer does not go looking for compose entries that do not exist.

Tasks 6–10 give conversion tables instead of repeating Task 5's endpoint code. That is total
information for a mechanical repetition: each row names the old route, the new route, the
request type dispatched, the success status and the policy, and each task lists its resource
records in full.

**Type consistency.** `HsmException` and its five subtypes (Task 2) are thrown by Tasks 3–14
and caught by the FHIR renderer (Task 2) and the Blazor UI services (Task 2 Step 6).
`FluentValidation.ValidationException` (Task 3) is thrown by `ValidationBehavior`, by seven
handlers in Task 2's reclassification table, and by `IdentityResultExtensions` (Task 11); it
is mapped in exactly two places — `HsmExceptionHandler` (400) and `FhirResponses` (422).
`PagedResult<T>` (Task 4) is produced by four queries and consumed by Tasks 5, 6, 7, 9 and the
three Blazor UI services. `PagingRules` (Task 3) is used by every paged query's validator.
`HsmUser` (Task 11) replaces `User` in every signature touched by Tasks 11–14 and in the test
factories. `IUserDirectory` (Task 11) has exactly three members and three consumers
(`ListUsersHandler`, `RequestActorFactory`, and Task 5's endpoint through the query).
`AuthenticatedClientAsync` (Task 1) keeps one signature across Tasks 5–14 and has its body
rewritten twice.

**Known ordering risks.**

1. **Tasks 2–10 run on auth machinery that Task 12 deletes.** Mitigated by the Task 1 seam and
   by Task 5's explicitly-temporary actor-installing middleware, which Task 12 Step 5 deletes
   by name. If a module task authenticates any other way, Task 12 will break it — the fix is
   to route it through the seam, not to patch the test.

2. **`ApiEnvelope` deliberately outlives Task 2.** Between Tasks 2 and 13 the tree contains two
   response shapes: problem+json errors everywhere, and the old success envelope on whichever
   `/v1` routes have not been reshaped yet. Task 13 Step 8 deletes the file and greps to prove
   it. If Tasks 5–10 are reordered such that `/v1/auth` is reshaped before the modules,
   `ApiEnvelope` can go earlier — but do not reorder them to reshape auth *first*, because
   auth is what the test seam signs in through.

3. **Task 11 is the one task that cannot be partially landed.** `HsmUser`, the
   `IdentityDbContext`, the handler sweep and the regenerated migration are one commit
   because each is unbuildable without the others. If it has to be abandoned mid-way, revert
   the whole commit rather than trying to leave half of it in.

4. **`Microsoft.Extensions.Identity.Stores` in `Hsm.Domain` weakens the leaf rule.** No project
   reference is added and `PortPurityTests`/`ContractsPurityTests` still pass, but a future
   reader will ask why the domain references a framework package. Task 17 records the answer
   in the conventions doc; if it is ever removed, the answer to remove it *with* is a mapped
   persistence user and a mapper, not a second entity that drifts.

5. **The two in-memory hosts must share a data-protection key ring** for the shell suites to
   pass in Task 12. This is a real deployment requirement, not a test artifact, and the failure
   mode — a cookie one host cannot decrypt — looks exactly like a bad password. Task 12 Step 10
   calls it out with the fix, and Task 17 records it as a convention.

6. **The `pr-gate` job name is load-bearing.** Task 1 edits `pr-validation.yml`. All three
   rulesets in `.github/rulesets/` reference `pr-gate` by name; renaming it silently un-gates
   `development`, `main` and `release/**`. Task 1 Step 7 says so; nothing else in this plan
   touches that file.

---

## Sources & References

- Design (requirements source): `docs/brainstorms/2026-08-03-standard-api-surface-design.md`
- Predecessor plan (style and depth template): `docs/plans/2026-07-28-001-refactor-clean-cqrs-three-host-plan.md`
- Conventions: `docs/reference/dotnet-conventions.md`
- Architecture decisions: `docs/ARCHITECTURE-DECISIONS.md` (§1 licensing, §5.1 monolith,
  §5.2 client isolation, §5.4 API contracts, §6 store roles, §9 sequencing, §11 decision log)
- Release bar: `docs/plans/2026-07-27-002-minor-release-definition-of-done.md`
- Frozen source, historical only: tag `freeze/typescript-2026-07-27`

