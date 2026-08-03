---
title: "Standard API surface: un-freeze the legacy contract"
type: design
status: approved
date: 2026-08-03
origin: brainstorm conversation 2026-08-03 (follows the merged Clean+CQRS restructure, PR #16)
supersedes_decisions: "frozen NestJS wire contract (envelope, exact messages, /v1 routes); bcrypt identity stack; Jobs naming"
---

# Standard API Surface — Design

## Premise

The custom JSON envelope (`{code, message, errors}`), exact error messages, `/v1` route
shapes, and the bcrypt/JWT identity stack all existed to reproduce the legacy NestJS
system byte-for-byte. The user declared the project **fully greenfield** (2026-08-03):
the frozen contract is no longer the spec. Clients adapt to the new API afterward.
Nothing is live in production; the dev database may be reset.

The Clean+CQRS core (dispatcher, pipeline behaviors, slices, queue, worker, three
hosts) **survives unchanged** — only the wire surface and the identity machinery change.

**Oracle constraint restated:** the app uses PostgreSQL only. The legacy Oracle
production database is read-only for any tooling that reaches it and is never touched
by this work.

## Decisions (all user-approved)

| Topic | Decision |
|---|---|
| Scope | Full resource redesign — paths, verbs, pagination, shapes |
| Branch strategy | ONE branch (`feat/standard-api-surface`), everything at once |
| Base path | `/api/v1/...`; FHIR stays `/fhir/R4` |
| Success bodies | Plain resource JSON; 200/201+Location/202/204 |
| Errors | RFC 9457 ProblemDetails (`application/problem+json`), single `IExceptionHandler` |
| Pagination | `PagedResult<T>` = `{items, page, pageSize, totalItems, totalPages}`; `page`/`pageSize` params |
| Validation | FluentValidation validators in the existing pipeline `ValidationBehavior`; failures → `ValidationProblemDetails` |
| Identity | **Full ASP.NET Core Identity** (UserManager/SignInManager, Identity tables, PBKDF2) |
| User data | Dev DB reset; migration baseline regenerated |
| Docs | Built-in `AddOpenApi()` + **Scalar** UI at `/api`; spec committed as artifact with generated==committed test |
| Renames | `Infrastructure/Jobs`→`Queue` (+ config keys); `Application/Auth`+`Api/Auth`→`Identity`; `IAuthUnitOfWork` retired into `IUnitOfWork` |
| Email model | "Batch" renamed to **email**: `POST /api/v1/emails` creates the send record |
| Dropped routes | `POST /send/sms` (stub), `POST /auth/pin/generate|validate` (frozen no-ops) |
| Tests | 261 contract tests retired; new `tests/Hsm.Api.Tests` pins the new surface |

## Section 1 — What changes, what stays

**Stays:** CQRS dispatcher + pipeline (Telemetry → Authorization → Validation →
Transaction), handler business logic, `Hsm.Domain` (except Identity), Redis queue,
worker, scheduler, telemetry config, Blazor screens + UI services (in-process dispatch),
FHIR endpoints + `OperationOutcome`, Docker/compose/CI skeleton.

**Replaced:**
- `Hsm.Api/Http/` (envelope, `ErrorStatusCodes`, `ApiErrorHandling`, `Validation`) — deleted.
- All `/v1/*` routes — redesigned per the route map.
- Hand-rolled `IValidator`/`ValidationFailure`/`BodyValidator` — FluentValidation.
- Custom auth machinery — ASP.NET Core Identity (Section 4).
- 261 contract tests + frozen OpenAPI snapshot — retired; new API tests + living spec.
- "frozen X" doc comments — deleted everywhere.

**New:** OpenAPI generation + Scalar; `PagedResult<T>` in `Hsm.Contracts`.

## Section 2 — Route map

Conventions: plural resources; no verbs in paths except explicit actions; no side
effects on GET; camelCase JSON; `page`/`pageSize`.

### Identity (`/v1/auth` → `/api/v1/identity`)

| Old | New |
|---|---|
| POST /auth/signup | POST /identity/register |
| POST /auth/login | POST /identity/login |
| GET /auth/logout | **POST** /identity/logout |
| GET /auth/refresh | **POST** /identity/refresh (integration tokens only — browser uses sliding cookie) |
| GET /auth/profile | GET /identity/me |
| POST /auth/onboarding | POST /identity/onboarding |
| GET /auth/csrf | GET /identity/csrf |
| POST /auth/password/forgot | POST /identity/password/forgot (rate limit stays) |
| POST /auth/password/reset | POST /identity/password/reset (rate limit stays) |
| POST /auth/username/recover | POST /identity/username/recover (rate limit stays) |
| POST /auth/signup/integration | POST /identity/integrations/register |
| POST /auth/logout/integration | POST /identity/integrations/logout |
| POST /auth/pin/* | dropped (frozen no-op stubs) |

### Users (`/v1/user` → `/api/v1/users`)

| Old | New |
|---|---|
| GET /user | GET /users (paged) |
| POST /user/staff | POST /users (admin; staff-role rule in validator) |
| GET /user/{id} | GET /users/{id} |
| PATCH /user/{id}/role | PATCH /users/{id} (body `{role}`) |
| PATCH /user/me | PATCH /users/me |
| POST /user/me/password | POST /users/me/password |

### Coms (`/v1/coms` → top-level)

| Old | New |
|---|---|
| POST /coms/send/email | POST /api/v1/emails ("batch" renamed to email) |
| GET /coms/emails/batches | GET /api/v1/emails (paged) |
| GET /coms/emails/batches/{id} | GET /api/v1/emails/{id} (includes recipient statuses) |
| POST /coms/emails/batches/{id}/resend | POST /api/v1/emails/{id}/resend |
| GET /coms/emails/recipients | GET /api/v1/email-recipients?email={id} (list views) |
| GET /coms/emails/recipients/{id} | folded into email detail; standalone GET stays if a screen needs it (planner verifies) |
| POST /coms/emails/recipients/{id}/resend | POST /api/v1/emails/{id}/recipients/{rid}/resend |
| POST /coms/send/sms | dropped until SMS is real |
| POST /coms/webhooks/{provider} | POST /api/v1/webhooks/{provider} (anonymous; HMAC is the credential — unchanged) |

### Documents (`/v1/docs` → `/api/v1/documents`)

| Old | New |
|---|---|
| GET /docs | GET /documents (paged) |
| POST /docs/upload (+ /docs/create — planner verifies the old split) | POST /documents (multipart upload) |
| POST /docs/generate | POST /documents/generated → 202 `{id, jobId}` |
| GET /docs/{id} | GET /documents/{id} |
| DELETE /docs/{id} | DELETE /documents/{id} |
| DELETE /docs (bulk, body ids) | DELETE /documents?ids=... |
| POST /docs/url (batch presign) | POST /documents/urls |
| GET /docs/{id}/url | GET /documents/{id}/url |

### Templates (`/v1/templates` → `/api/v1/templates`)

CRUD unchanged in shape; `GET /{identifier}` → `GET /{id}` (single identifier form);
actions stay explicit: POST /templates/validate, POST /templates/draft-render.

### Settings, Health, System

- GET + PUT /api/v1/settings; GET /api/v1/settings/audit.
- Built-in `MapHealthChecks("/health")` (standard liveness middleware).
- GET /api/v1/system/status — existing anonymous dashboard query, includes version.

### FHIR — untouched: /fhir/R4/Patient (search, read, create).

### Docs UI — GET /api → Scalar; /api/openapi.json → generated spec.

## Section 3 — Error + validation model

- Success: resource JSON. 200 read/update, 201+Location create, 202 async, 204 delete.
- Errors: one `IExceptionHandler` in `Hsm.Api` + `AddProblemDetails()`. Always `type`,
  `title`, `status`, `traceId`; `detail` only when it adds information.
- `ApiException` (status-code catch-all) is deleted. Closed exception set in
  `Hsm.Application/Errors/`:

| Exception | HTTP |
|---|---|
| FluentValidation `ValidationException` | 400 `ValidationProblemDetails` (`errors: {field: [messages]}`) |
| `UnauthorizedException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException(resource, id)` | 404 |
| `ConflictException` | 409 |
| rate limiter / `TooManyRequestsException` | 429 |
| anything else | 500 (generic; detail in logs only) |

- `AuthorizationBehavior` throws the new exceptions — pipeline stays transport-independent;
  Blazor UI services catch the same types.
- Validation single home: FluentValidation `AbstractValidator<TCommand>`, assembly-scanned,
  executed by the pipeline `ValidationBehavior`. HTTP, Blazor, queued dispatch validate
  identically. Malformed JSON/binding failures use ASP.NET's built-in 400 problem response.
- FHIR carve-out: same exceptions, rendered as `OperationOutcome` (existing `FhirResponses`
  retargeted).

## Section 4 — ASP.NET Core Identity

**Membership:**
- `HsmUser : IdentityUser<Guid>` with profile fields (names, `OnboardingCompletedAt`,
  `DeletedAt`, …). `HsmDbContext : IdentityDbContext<HsmUser, IdentityRole<Guid>, Guid>`;
  table names mapped to snake_case repo convention.
- All 10 roles seeded as Identity roles (admin, doctor, nurse, technician, therapist,
  pharmacist, patient, family, integration, developer).
- PBKDF2 hasher; bcrypt + SHA-256 pre-digest deleted.
- `UserManager`/`SignInManager` replace custom stores/hashing/lockout; `IdentityStores.cs`
  mostly deleted.
- Password reset + username recovery via Identity token providers. Enumeration-safe
  responses and recovery rate limits STAY (behavior, not machinery).

**Authentication (standard middleware, two schemes):**
- Browser: Identity application cookie — encrypted, HttpOnly, SameSite=Strict, sliding
  expiration. Replaces JWT-in-cookie + refresh rotation for browsers. CSRF: standard
  ASP.NET antiforgery (XSRF-TOKEN cookie + header); `GET /identity/csrf` returns token.
- Integrations: `AddJwtBearer` validation; access tokens issued by `/identity/integrations`
  endpoints; refresh = opaque high-entropy tokens, SHA-256 at rest, rotation-on-use.
  Integration accounts stay user rows with `integration` role.

**Unchanged:** pipeline is the sole authorizer (`[RequireRole]` on commands,
`AuthorizationBehavior`, `RequestActor`); `RequestActorFactory` reads Identity claims.
Queue envelope actor unchanged.

**Schema:** `Initial` migration regenerated from scratch (Identity tables + everything;
citext/jsonb/filtered indexes preserved where entities survive). Dev databases dropped once.

**Out:** 2FA, external logins, Identity UI pages.

**Accepted behavior changes (greenfield):** browser refresh rotation → sliding cookie;
PBKDF2; Identity lockout semantics.

## Section 5 — Renames, docs, testing

**Renames:**
- `Infrastructure/Jobs` → `Infrastructure/Queue`; namespace `Hsm.Infrastructure.Queue`;
  config keys `Jobs:*` → `Queue:*` (appsettings, secrets.env.template, compose).
- `Application/Auth` + `Api/Auth` → `Identity`.
- `IAuthUnitOfWork` retired — `IUnitOfWork` gains the flush method.
- Conventions doc + ARCHITECTURE-DECISIONS updated in-branch; obsolete drift entries pruned.

**OpenAPI/Scalar:**
- `AddOpenApi()` + endpoint metadata (summaries, `Produces<T>`, per-module tags).
- Scalar at `/api`; config-gated `OpenApi:Enabled` — on in Development, off by default in
  production.
- Generated spec committed at `docs/reference/openapi.json`; test asserts
  generated == committed (surface changes force a conscious spec update).

**Testing:**
- New `tests/Hsm.Api.Tests` (WebApplicationFactory on `Hsm.Api`): per-route status codes,
  ProblemDetails shapes, validation shape, full 401/403 auth matrix, pagination,
  cookie/antiforgery behavior, openapi==committed gate. Shell tests re-homed or kept —
  planner's call.
- Kept: `Hsm.Tests` (policy-closure snapshot updated for renames), `Hsm.Integration.Tests`,
  `Hsm.Web.Tests`.

## Out of scope

Client (Angular) adaptation · SMS · 2FA/external logins · dead-letter ops tooling ·
making deploy.yml real.

## Open items for the planner

- Old `POST /docs/create` vs `/docs/upload` split — verify what create did; unify under
  `POST /documents` or keep a second route if genuinely different.
- Standalone email-recipient GET — keep only if a Blazor screen needs the flat list.
- Shell contract-test factory topology after `Hsm.Api.Tests` replaces `Hsm.Contract.Tests`.
