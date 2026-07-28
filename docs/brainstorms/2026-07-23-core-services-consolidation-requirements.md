---
title: Legacy core microservices → monolith consolidation (v1 minor) - Requirements
type: feat
date: 2026-07-23
status: superseded
superseded_by: docs/brainstorms/2026-07-27-dotnet-blazor-stack-pivot-requirements.md
module: apps/backend/api, apps/backend/worker, packages/database, packages/config, packages/common, packages/storage
tags: [architecture, backend, consolidation, hexagonal, ports-and-adapters, oracle, fhir, microservices]
---

# Legacy core microservices → monolith consolidation (v1 minor)

> **SUPERSEDED — as to delivery vehicle.** The TypeScript monorepo this
> document targeted (`@hsm/api` + `@hsm/worker`) was deliberately killed as
> the delivery vehicle for this minor: it was frozen at tag
> `freeze/typescript-2026-07-27` and the minor was rebuilt on
> .NET/ASP.NET Core per
> `docs/brainstorms/2026-07-27-dotnet-blazor-stack-pivot-requirements.md` and
> `docs/plans/2026-07-27-001-feat-dotnet-blazor-rewrite-plan.md`.
> The **contract scope defined here carried forward into C#** — the six legacy
> microservices' external surface remains the specification, now pinned by the
> frozen OpenAPI snapshot in `docs/reference/` and the contract-test suite.
> Module paths below refer to the frozen TypeScript tree.

## Problem Frame

Six legacy NestJS microservices under the GitHub org **HSM-SANOSOFT**
(`hsm-be-core-{auth,users,coms,docs,common,gateway}`) implement the hospital's
current API surface. They are **API-only, never intended for a UI**, and their
internal logic is not ideal — thin Oracle adapters (raw `oracledb` SQL over TCP
message patterns, no auth guards, no validation DTOs), aggregated by a gateway
that proxies over a message bus.

The goal is to **retire that logic** and reproduce the **external contract
(endpoints + what they return)** inside the monorepo's single backend app
(`@hsm/api` + `@hsm/worker`), rebuilt with this repo's conventions and
architecture. Getting these capabilities working end-to-end is the **first minor
version release**. The monorepo was already scaffolded to receive them
(module homes exist under `modules/core/*` and `modules/security/*`).

This is not a code port. The legacy repos are a **specification** — the
authoritative source of *which endpoints exist and what they return* — not code
to copy.

---

## Actors

- **A1 — Integration / machine caller.** The legacy app's replacement and other
  backend systems. Authenticate with a long-lived API token (bearer). Primary
  consumer of these endpoints.
- **A2 — Staff user (browser).** Cookie/CSRF session; may call a subset via the UI.
- **A3 — Admin.** Provisions integration accounts / API tokens.
- **A4 — Patient (data subject).** Not a direct API actor; keyed by national ID
  (Ecuadorian *cédula*). Their data is read (Oracle) and consent is recorded.

---

## Key Flows

- **F1 — OTP (second-verification PIN):** issue a one-time PIN for a cédula, then
  validate it with attempt-throttling + lockout.
- **F2 — Patient lookup:** fetch a patient's demographic record by national ID.
- **F3 — Data-protection consent (LOPD):** read / create / update a patient's
  personal-data-processing consent.
- **F4 — Clinical document:** render a clinical form (`hcu_053`, `hcu_005`) to PDF
  and retrieve the newest generated document by (type, id).
- **F5 — Communications:** send/resend transactional email; send SMS.
- **F6 — Reference data:** list chatbot menus, attention types, and chat-service
  catalog.
- **F7 — Chat statistics:** aggregate Chatwoot conversation/message counts.

---

## Requirements

### Foundations (built first — shared by the feature requirements)

- **R9. Read-only Oracle access via a port.** A domain **port** exposes
  read-only queries; an **adapter** re-wires the currently-inert Oracle datasource
  (`synchronize:false`, no write methods) against the legacy tables. No app code
  reads Oracle directly. Env prefix `DB_ORACLE_*` (precedent exists).
- **R10. API-token + cookie auth.** Machine callers (A1) authenticate with a
  long-lived API token built on the existing **Integration account** plumbing
  (`RolesEnum.System.Integration`, bearer JWT, Admin-provisioned); browser callers
  (A2) use the existing cookie/CSRF session. No net-new opaque-key store for v1.
- **R11. Postgres baseline migration.** A committed `0000-init-schemas` + baseline
  migration so new owned-state entities can ship to non-dev (the baseline is
  currently missing — only `.gitkeep`).

### Features (the reproduced contract)

- **R1. OTP endpoints.** `POST /v1/auth/pin/generate` + `POST /v1/auth/pin/validate`
  (fill the existing controller stubs). OTP state is **owned in Postgres** (not the
  legacy Oracle `PDP_LOG_SEGUNDA_VERIFICACION`); delivery goes through the
  Email/SMS ports. Attempt-throttling + lockout preserved. (F1)
- **R2. Patient lookup.** Serve patient demographics by national ID as a
  **FHIR-compliant `Patient`** resource via `GET /v1/fhir/R4/Patient?identifier=…`,
  sourced from Oracle `PACIENTES` **read-only** through an anti-corruption
  translator behind the patient port. (F2)
- **R3. Data-protection consent (LOPD).** Read / create / update consent as a
  **FHIR-compliant `Consent`** resource, **owned in Postgres**. Replaces legacy
  `GET/POST/PUT /users/LOPD`. (F3)
- **R4. Clinical documents.** Reproduce `POST /docs` (generate) + `GET /docs/:type/:id`
  (newest presigned URL) using the **existing** docs pipeline (worker Puppeteer +
  Handlebars → S3 presigned URL). The `hcu_053` / `hcu_005` forms become
  **DOCS-category Templates**; the generated-document registry is the existing
  `DocumentsEntity` (Postgres), not Oracle `DOCUMENTOS_GENERADOS`. (F4)
- **R5. Email.** Reproduce email send + resend on the **existing** coms module
  (API validates + persists + enqueues; worker sends via the Email port). (F5)
- **R6. SMS.** Fill the existing `POST /v1/coms/send/sms` stub: a provider-agnostic
  **SMS port** with a worker adapter (provider is not fixed — Masiva is not
  required), SMS templates, and an owned Postgres record. (F5)
- **R7. Reference data.** `GET /v1/reference/*` for chatbot menus, attention types,
  and chat services — **Oracle read-only** (via R9). (F6)
- **R8. Chatbot statistics.** `POST /v1/reference/chatbot/statistics` aggregating
  **Chatwoot** conversation/message counts through a Chatwoot adapter. (F7)

### Cross-cutting

- **R12. Contract shape.** All endpoints are **`/v1`-versioned** and **require
  auth**. Non-FHIR responses use the repo's `data`/`metadata` success envelope and
  `issue`/`ApiErrorCode` error envelope; patient/consent responses are **FHIR-
  shaped** (FHIR routes bypass the envelope, per existing convention). Responses
  are **not** byte-for-byte legacy JSON — the legacy consumers will be updated to
  the new contract.
- **R13. Ports & adapters (hexagonal).** Every external dependency (Oracle,
  SMS/email providers, Chatwoot, storage) is a **domain port** with an
  infrastructure **adapter**. Legacy/provider specifics never leak into domain
  code. This is the standing architectural convention for the app.
- **R14. No Oracle writes.** Oracle is **read-only** (patient/reference data);
  all owned write-state (OTP, consent, SMS records, document registry) lives in
  Postgres/`@hsm/database`.

---

## Acceptance Examples

- **AE1 (R1/F1).** Generating a PIN for a cédula with no pending challenge creates
  one Postgres OTP row and triggers delivery; a second generate before expiry
  returns the same pending challenge (idempotent), not a second row.
- **AE2 (R1/F1).** Validating with the wrong code increments the attempt counter;
  after the configured max failures the challenge is locked and further attempts
  are rejected; a correct code within the limit marks it used.
- **AE3 (R2/F2).** `GET /v1/fhir/R4/Patient?identifier=<system>|<cédula>` returns a
  FHIR `Patient` mapped from Oracle `PACIENTES`; an unknown cédula returns an empty
  FHIR searchset (not a 500), and **no Oracle write occurs**.
- **AE4 (R3/F3).** Creating a consent for a (cédula, channel) that already has one
  returns a conflict; updating an existing consent changes only its status +
  timestamp; all consent state is in Postgres.
- **AE5 (R4/F4).** `POST` a `hcu_053` payload renders a PDF, stores it in S3, and
  returns a presigned URL + id; `GET` for that (type, id) returns a fresh presigned
  URL to the newest version.
- **AE6 (R6/F5).** `POST /v1/coms/send/sms` with a known template enqueues a job the
  worker delivers through the SMS port and records in Postgres; an unknown template
  is rejected with a validation error.
- **AE7 (R7/F6).** `GET /v1/reference/attention-types` returns the active list read
  from Oracle read-only, wrapped in the success envelope.
- **AE8 (R10).** A request with a valid integration API token is authorized; an
  anonymous request to the same endpoint is rejected with `COMMON.UNAUTHORIZED`.

---

## Scope Boundaries

- **In scope:** the `core` slice only — the six named services' external contract,
  rebuilt on the monorepo platform per the requirements above.
- **Out of scope (`his` / `has`):** `Paciente`/`Consentimiento` HIS-Oracle beyond
  the R2 lookup, and the HAS domain (`postulantes`, `trabajos`, `transaccion`,
  `recaudo`, `compensacion`) — these are aspirational in the legacy gateway, **not
  implemented**, and excluded.
- **No UI.** These are API endpoints; the Angular app is a separate track.
- **No byte-for-byte legacy JSON.** Contract is adapted to the `/v1` + envelope +
  auth model; legacy consumers migrate.
- **No Oracle writes.** Read-only only.
- **Provider specifics are adapter config.** SMS provider and Chatwoot wiring are
  swappable behind ports; no specific vendor is required by v1.

### Deferred to Follow-Up Work

- Angular SSR plan **Track 4** (U13–U15 config-ownership / Infisical partition) is
  **paused**; its per-module config-ownership work folds into this consolidation
  later rather than running in parallel.
- A net-new opaque API-key store with per-key scopes (v1 reuses Integration JWTs).
- Broader HIS/HAS domains.

---

## Key Decisions (carried into planning)

- **Reuse over rebuild.** Email, docs/PDF, templates, settings, auth, and storage
  already exist "our way"; most features are gap-fills or net-new read endpoints,
  not ports of legacy code.
- **Reclassification.** Legacy `/users/*` are **patient-data + consent** keyed on
  cédula — they map to the **clinical/patient (FHIR)** domain and a **consent**
  resource, **not** the staff `users` module (username/password accounts).
- **FHIR for patient-facing data.** Patient and consent are exposed as FHIR R4
  resources; Oracle patient data is wrapped by an anti-corruption translator so the
  legacy DB is usable through the new FHIR interface.
- **Build order = one service at a time** (foundations first), each verified before
  the next.

---

## Open Questions (for planning)

- **Consent as FHIR `Consent` vs a simple domain entity.** Modeling LOPD as FHIR
  `Consent` is consistent with R2/R13 but heavier; confirm during planning.
- **Reference module home + naming.** New `modules/core/reference` (English) vs
  keeping legacy Spanish route names.
- **Exact auth gate per endpoint.** Integration-role-only vs any authenticated
  vs admin — resolve per endpoint during planning.
- **SMS/Chatwoot provider choice** — deferred; ports make it swappable.

---

## Sources & References

- Legacy repos (HSM-SANOSOFT): `hsm-be-core-{auth,users,coms,docs,common,gateway}`
  (cloned as siblings of `hsm-app` for contract harvest).
- Monorepo platform: `apps/backend/api/src/modules/{core,security,clinical}`,
  `packages/{database,config,storage,common}`, and the four workspace `CLAUDE.md`s.
- Contract harvest + platform mapping: performed via research agents (this session).
