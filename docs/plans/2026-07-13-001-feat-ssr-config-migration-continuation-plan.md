---
title: Angular SSR + Config Migration (Track 2–4 continuation) - Plan
type: feat
date: 2026-07-13
origin: docs/plans/2026-07-10-001-feat-angular-ssr-auth-migration-plan.md
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: legacy-requirements
execution: code
module: apps/frontend/web, apps/backend/api, packages/config, packages/database
tags: [architecture, frontend, ssr, angular, config, infisical, transfer-state]
---

# Angular SSR + Config Migration (Track 2–4 continuation) - Plan

## Goal Capsule

**Objective.** Continue the Angular SSR / dual-transport migration from its parent
plan (`docs/plans/2026-07-10-001-feat-angular-ssr-auth-migration-plan.md`).
**Track 1 (auth — U1–U5) is COMPLETE and shipped** on branch
`feat/ssr-auth-migration` (commits `71299bc`, `2e105a5`, `e251ada`, `ac42545`,
`4585d04`): dual-transport cookie/bearer auth with signed double-submit CSRF,
verified green (103 API tests, 263 web tests). This plan carries the remaining
**Track 2 (SSR enablement, U6–U9)**, **Track 3 (config via transfer state,
U10–U12)**, and **Track 4 (config ownership + settings seed + Infisical,
U13–U15)**.

**Authority hierarchy.** The parent plan is the decided WHAT/HOW; this document
restates only the un-started units with the review decisions already folded in.
Repo conventions (per-workspace `CLAUDE.md`) and the Oracle SELECT/UPDATE-only
constraint override plan details on conflict. The API's Bearer contract remains
**inviolable** — every change stays additive.

**Decided (carried from the parent plan's review, do not re-litigate).**
- **Deployment is same-site:** web and API are two subdomains of one shared
  registrable parent domain (`<web>.<domain>` + `<api>.<domain>`); the session
  cookie uses `Domain=<parent>` via `COOKIE_DOMAIN`. No BFF, no `SameSite=None`,
  no framework switch. Access cookie `SameSite=Lax`, refresh `SameSite=Strict`.
- **SSR refresh model (a) — read-only probe:** SSR renders off the access cookie
  only and never refreshes/rotates the RT server-side; on an absent/expired
  access cookie it renders a neutral authenticated shell (not a hard login
  redirect) and the client refreshes after hydration.

**Stop conditions.** Surface a blocker rather than guessing when: an SSR guard
would require rewriting a feature rather than platform-guarding it; a Track-4
step needs a destructive Infisical operation (Track 4's live secret-store
mutations are executed by a human operator, not autonomously).

**Execution profile.** Sequenced by track: Track 2 (U6–U9) → Track 3 (U10–U12)
→ Track 4 (U13–U15). Track 4 (U13) may land independently after Track 1 (already
done) but is sequenced last to avoid churn.

---

## Product Contract

### Summary

Adopt `@angular/ssr` (keep Angular 21 zoneless) so first paint renders on infra
we control — the win for low-end hospital hardware — now that the browser
session is cookie-backed (Track 1). Retire the runtime `config.json` machinery
for the SSR Node server reading `process.env` + Angular transfer state, and
partition Infisical `/app` by consuming package/module.

### Requirements (remaining)

Requirements R1–R6 (auth) are satisfied by the shipped Track 1. This plan
delivers R7–R16.

**SSR (Track 2)**
- R7. `@angular/ssr` is enabled (Angular 21, zoneless SSR) with server routes and
  incremental hydration.
- R8. Browser-only code is platform-guarded so SSR bootstrap does not touch
  `window`/`document`/`localStorage`: Monaco, the Handlebars client preview, the
  `hsm.lang`/`hsm.lastUsername` reads, and the `bootLocaleId` provider-time
  `localStorage` read. The Transloco loader resolves an absolute URL server-side.
- R9. On server-side data calls the SSR server forwards the incoming cookie to the
  API; guards run server-side off the **access** cookie as a **read-only probe**
  (SSR never refreshes/rotates — model (a)). Absent/expired access cookie →
  neutral authenticated shell, client refreshes after hydration.
- R10. The web image entrypoint runs the SSR Node server instead of a static file
  server.

**Config (Track 3)**
- R11. The SSR server reads `process.env` and passes non-secret config to the
  client via transfer state; the boot `config.json` fetch, `scripts/gen-config.mjs`,
  and the config app-initializer are removed.
- R12. API base URL is **host only** (e.g. `http://localhost:4201`); the API
  version (`/v1`, `/v2`) is chosen per endpoint by the caller, not baked into the
  base.
- R13. UI version comes from a build-time file/endpoint (git SHA), symmetric with
  the API's `/v1/health/version` — not a `WEB_APP_VERSION` env var.
  `WEB_PRODUCTION` is dropped (the SW toggle uses `!isDevMode()`).

**Config ownership + Infisical (Track 4)**
- R14. Partition Infisical `/app` by **consuming package/module**: `/shared`
  (`ENVIRONMENT`), `/database` (`DB_POSTGRES_*`), `/queue` (`DB_REDIS_*`),
  `/storage` (`STRG_S3_*`), `/api` (`JWT_*`, `COOKIE_*`, `CSRF_SECRET`,
  `APP_BASE_URL`, `SWAGGER_*`, `SMTP_WEBHOOK_KEY`, `DEFAULT_ADMIN_*`), `/worker`
  (`SMTP_*`), `/web` (`WEB_API_BASE_URL`, `WEB_PORT`). One read-scoped machine
  identity per service; dev/staging/prod stay the orthogonal environment axis.
- R15. **Invert config ownership in `@hsm/config`**: each package/app validates its
  own env slice via its own `envs` (Joi retained). `base` shrinks to
  `ENVIRONMENT`; `@hsm/database` owns `DB_POSTGRES_*`, `@hsm/queue` owns
  `DB_REDIS_*`, `@hsm/storage` owns `STRG_S3_*`, worker owns `SMTP_*`, api owns
  `JWT_*`/`COOKIE_*`/`CSRF_SECRET`/`APP_BASE_URL`/`SWAGGER_*`/`SMTP_WEBHOOK_KEY`/
  `DEFAULT_ADMIN_*`. No service holds a var it does not consume.
- R16. **Invert the settings-seed system**: `@hsm/database` keeps only the settings
  table + `SettingsAccessor`; each module registers its own seed-able settings
  from its own validated `envs`. Seeds are **insert-if-absent (never update)** and
  **idempotent** (`INSERT ... ON CONFLICT (key) DO NOTHING`). Rename
  `COMS_WEBHOOK_SIGNING_KEYS` → `SMTP_WEBHOOK_KEY` across code + tests in lockstep.

### Scope Boundaries

- **In scope:** SSR enablement + platform guards, config-via-transfer-state,
  config-ownership inversion, Infisical restructure.
- **Not a framework switch.** Angular stays (same-site deployment makes the cookie
  model work without a BFF — see Decided).
- **Not a thin-client rewrite.** SSR moves first render + enables deferred
  hydration; the app still runs client-side JS after hydration.

#### Deferred to Follow-Up Work
- Prerendering / static server routes for public pages (SSR is dynamic-render
  first).
- Server-side data-fetch resolvers beyond what R9 needs to render authenticated
  shells.
- Broader secret-reference cleanup in Infisical beyond the seven-folder partition.

---

## Planning Contract

### Key Technical Decisions

- **KTD5 — Platform-guard, don't rewrite.** Browser-only sites (R8) are wrapped
  with `isPlatformBrowser` / `afterNextRender` / early `typeof window` returns —
  not re-architected. `afterNextRender`-based Monaco loading is already SSR-safe;
  the highest-risk site is `bootLocaleId()` reading `localStorage` at
  provider-construction time (`app.config.ts`), which runs during SSR bootstrap.
- **KTD6 — API base URL host-only; version per-endpoint.** `ApiClient.url()`
  currently prefixes with a base that already includes `/v1`. Split so the base is
  host-only and each call names its version segment. Transfer state carries only
  the host. **New Track-1 consumers to migrate too:** `CsrfService`
  (`${config.apiBaseUrl}/auth/csrf`) and `AuthRefreshClient`
  (`${config.apiBaseUrl}/auth/refresh`) — both currently assume `/v1` in the base.
- **KTD7 — Config via transfer state, keyed off the SSR server's `process.env`.**
  The SSR Node server reads env natively and injects non-secret config (host-only
  API base) into the render via Angular `TransferState`; `ConfigService` reads
  from transfer state instead of a fetched `config.json`.
- **KTD8 — SSR read-only probe (model a).** SSR authenticates off the forwarded
  access cookie only; it never rotates the RT (so the path-scoped refresh cookie
  never needs to reach SSR and no rotated `Set-Cookie` can be lost). Expired AT →
  neutral shell, client refreshes post-hydration.

### Assumptions

- Same-site deployment (Decided) — the forwarded-cookie SSR→API call and the
  browser transport are same-site; `main.ts` sets `credentials: true` /
  `origin: envs.APP_BASE_URL`.
- Server-to-server SSR→API base URL may differ from the browser's base URL (Open
  Question — resolve during U8/U10).
- Transloco catalogs stay served from `public/i18n/{es,en}.json`; SSR resolves an
  absolute URL to them server-side.

### Sequencing

Track 2 (U6→U7→U8→U9) → Track 3 (U10→U11→U12) → Track 4 (U13→U14→U15). Track 3's
host-only refactor (U11) depends on SSR transfer state (U10). U15 is
operator-executed and additionally gated on U9/U10 for the `/web` folder.

---

## Implementation Units

### U6. SSR — enable `@angular/ssr` and the server entry

- **Goal:** Add `@angular/ssr` with a server bootstrap, server routes, and
  incremental hydration; wire the builder. (R7)
- **Requirements:** R7.
- **Dependencies:** none (Track 1 done — auth is cookie-backed).
- **Files:** `apps/frontend/web/package.json` (`@angular/ssr`,
  `@angular/platform-server`, `express`), `apps/frontend/web/angular.json`
  (server/ssr/outputMode options), `apps/frontend/web/src/main.server.ts`,
  `apps/frontend/web/src/server.ts`, `apps/frontend/web/src/app/app.config.server.ts`
  (`provideServerRendering`), `apps/frontend/web/src/app/app.config.ts`
  (`provideClientHydration` with incremental hydration).
- **Approach:** Follow Angular 21 first-party SSR scaffolding for a zoneless app.
  Add server routes and incremental hydration to defer client JS. This unit only
  stands SSR up; browser-only guards land in U7 and cookie forwarding in U8.
- **Execution note:** Mostly scaffolding/config; prefer a build + SSR runtime
  smoke check (`ng build`, run the server, curl a rendered route) over unit
  coverage. Verify each generated file lands correctly (the harness may need
  re-verification via `git`/`ls`).
- **Test scenarios:** none — scaffolding/config; proven by the build + SSR smoke
  verification below.
- **Verification:** `pnpm --filter @hsm/web build` produces a server bundle;
  running the SSR server returns server-rendered HTML for a public route (e.g.
  `/login`).

### U7. SSR — platform-guard browser-only code

- **Goal:** Guard every browser-only site so SSR bootstrap never touches
  `window`/`document`/`localStorage`, and the Transloco loader resolves an absolute
  URL server-side. (R8)
- **Requirements:** R8.
- **Dependencies:** U6.
- **Files:** `apps/frontend/web/src/app/app.config.ts` (`bootLocaleId()`
  localStorage read — highest risk), `apps/frontend/web/src/app/core/i18n/language.service.ts`
  (`hsm.lang`), `apps/frontend/web/src/app/features/auth/login/login.ts`
  (`hsm.lastUsername`, `LAST_USERNAME_KEY`), `apps/frontend/web/src/app/core/i18n/transloco-loader.ts`
  (absolute URL server-side), `apps/frontend/web/src/app/features/templates/monaco-editor.ts`
  + `apps/frontend/web/src/app/core/editor/monaco-setup.ts` (confirm guards under
  SSR), `apps/frontend/web/src/app/features/templates/template-preview.util.ts` +
  `template-editor.ts` (Handlebars/`Blob`/`URL.createObjectURL`),
  `apps/frontend/web/src/app/core/pwa/*` (verify already window-guarded),
  `apps/frontend/web/src/app/features/documents/documents.ts`
  (`document.createElement('a')` download).
- **Approach:** Wrap provider-time reads with `isPlatformBrowser(inject(PLATFORM_ID))`;
  wrap component-time browser work in `afterNextRender`. The Transloco
  `HttpClient.get('/i18n/..')` needs an absolute base under SSR (inject a server
  base or resolve from the request origin).
- **Test scenarios:**
  - Covers R8. `bootLocaleId()` returns a safe default under a server platform (no
    `localStorage` access).
  - Language service read/write is a no-op server-side and functional in the
    browser.
  - Transloco loader fetches `en`/`es` catalogs under SSR via an absolute URL.
  - Monaco/Handlebars preview do not execute during server render.
- **Verification:** SSR render of an authenticated route produces no `window is not
  defined`/`localStorage is not defined` errors; `pnpm --filter @hsm/web test`
  green.

### U8. SSR — forward cookies to the API on server-side calls (read-only probe)

- **Goal:** On server-side data calls the SSR server forwards the incoming
  request's cookie to the API; guards run server-side off the access cookie as a
  read-only probe. (R9)
- **Requirements:** R9.
- **Dependencies:** U7 (U2 shipped).
- **Files:** `apps/frontend/web/src/server.ts` (capture the request), an SSR
  request-context provider + an HTTP interceptor branch that, when running
  server-side, attaches the forwarded `Cookie` header (a new server interceptor or
  a branch in `apps/frontend/web/src/app/core/auth/auth.interceptor.ts`),
  `apps/frontend/web/src/app/app.config.server.ts` (provide the request token).
- **Approach:** Provide the incoming `Cookie` header via a **request-scoped**
  injection token available only in the server config; the HTTP layer attaches it
  to outbound API calls during server render so the API's cookie extractor (U2)
  authenticates the SSR render. **Model (a): SSR is a read-only probe — it renders
  off the access cookie only and never refreshes/rotates the RT server-side.** On
  absent/expired access cookie it renders a neutral authenticated shell (not a hard
  login redirect); the client refreshes after hydration.
- **Isolation invariant (S5, non-negotiable):** the inbound `Cookie` must be bound
  to a **request-scoped** injection token — never a shared/singleton provider or
  module-level variable — because the long-lived SSR Node process serves many
  users concurrently and a shared provider would bleed one user's session into
  another render's outbound calls or HTML. Constrain the SSR→API base URL to a
  trusted internal host.
- **Test scenarios:**
  - Covers R9. During server render, outbound API calls carry the forwarded
    `Cookie` header.
  - A valid access cookie renders the authenticated shell server-side (no
    post-hydration login flash).
  - An expired/absent access cookie renders a neutral authenticated shell (not a
    hard redirect); SSR never rotates the RT.
  - **S5:** two concurrent renders with different session cookies never cross
    outbound `Cookie` headers (request-scoped isolation test).
- **Verification:** curl with a valid session cookie against the SSR server returns
  the authenticated shell in the initial HTML; `pnpm --filter @hsm/web test` green.

### U9. SSR — web image runs the Node server

- **Goal:** Replace the static `serve -s dist` runtime with the SSR Node server.
  (R10)
- **Requirements:** R10.
- **Dependencies:** U8.
- **Files:** `apps/frontend/web/Dockerfile` (runtime stage runs the SSR server),
  `docker/docker-compose.yaml` (web command/entry if referenced),
  `apps/frontend/web/package.json` (`serve:ssr`/start script).
- **Approach:** Runtime stage runs `node dist/server/server.mjs` (or the emitted
  server entry) on the existing web port; drop the `gen-config.mjs` invocation here
  (Track 3, U12). Keep `EXPOSE` and port mapping consistent with the port map.
- **Execution note:** Config/packaging; verify by running the image and hitting a
  rendered route.
- **Test scenarios:** none — image/runtime config; proven by the container smoke
  check.
- **Verification:** the built web image serves server-rendered HTML on its port.

### U10. Config — transfer-state config from the SSR server's env

- **Goal:** The SSR server reads `process.env` and injects non-secret config via
  Angular transfer state; `ConfigService` reads from transfer state. (R11)
- **Requirements:** R11.
- **Dependencies:** U8.
- **Files:** `apps/frontend/web/src/server.ts` (read env, seed a config value),
  `apps/frontend/web/src/app/app.config.server.ts` (provide config into
  `TransferState`), `apps/frontend/web/src/app/core/config/config.service.ts` (read
  from `TransferState`), `apps/frontend/web/src/app/core/config/config.schema.ts`
  (validate the transferred shape), `apps/frontend/web/src/app/core/config/config-testing.ts`
  (test seam).
- **Approach:** On the server, read the host-only API base from `process.env` (e.g.
  `WEB_API_BASE_URL`), put it in `TransferState`; the browser reads it back during
  hydration. `ConfigService` keeps its getter surface but sources from transfer
  state — no boot fetch. The config app-initializer removal happens in U12.
- **Test scenarios:**
  - Covers R11. `ConfigService.apiBaseUrl` resolves from `TransferState` with no
    network fetch.
  - Missing/invalid transferred config fails validation loudly.
  - The test seam (`provideTestConfig`) still supplies a base for unit tests.
- **Verification:** rendered HTML embeds the transfer-state config block; the
  browser reads the host without fetching `config.json`.

### U11. Config — host-only API base, version per-endpoint

- **Goal:** Split the API base into host-only, with each call naming its version
  segment; UI version from a build-time file. (R12, R13)
- **Requirements:** R12, R13.
- **Dependencies:** U10.
- **Files:** `apps/frontend/web/src/app/core/api/api-client.ts` (`url()` builds
  `${host}/${version}/...`), all `ApiClient` callers that assumed `/v1` in the base
  (auth endpoints in `auth.service.ts`; **`csrf.service.ts` and the
  `AuthRefreshClient` in `auth.interceptor.ts` — new Track-1 consumers that build
  `${config.apiBaseUrl}/auth/...`**; feature API calls),
  `apps/frontend/web/src/app/core/version/version.service.ts` (read a build-time
  version file/`version.json` instead of `appVersion` env), a build step emitting
  the git-SHA version file.
- **Approach:** `ApiClient.url(version, path)` (or a per-call version arg) so `/v1`,
  `/v2` are chosen per endpoint (KTD6). The transferred base is host-only.
  `VersionService.uiVersion` reads a build-time file symmetric to the API's
  `/v1/health/version`.
- **Test scenarios:**
  - Covers R12. `ApiClient.url()` composes host + explicit version; two versions
    produce two different URLs from one base.
  - Auth/CSRF/refresh endpoints resolve to `/v1/auth/...` after the split.
  - Covers R13. `VersionService.uiVersion` returns the build-time SHA, not an env
    value.
- **Verification:** `pnpm --filter @hsm/web test` green; API calls hit correct
  versioned URLs against a host-only base.

### U12. Config — remove config.json machinery; drop WEB_PRODUCTION

- **Goal:** Delete `config.json` generation/fetch and the config app-initializer;
  drop `WEB_PRODUCTION`. (R11, R13)
- **Requirements:** R11, R13.
- **Dependencies:** U10, U11.
- **Files:** remove `apps/frontend/web/scripts/gen-config.mjs`,
  `apps/frontend/web/public/config.json.example`;
  `apps/frontend/web/src/app/app.config.ts` (remove the config-fetch
  `provideAppInitializer`); `apps/frontend/web/package.json` (remove
  `predev`/`gen-config` scripts); `apps/frontend/web/Dockerfile` (remove the
  `gen-config.mjs` runtime call — already partly handled in U9);
  `apps/frontend/web/angular.json` and SW toggle sites using `WEB_PRODUCTION` →
  `!isDevMode()`.
- **Approach:** With transfer state in place (U10), the boot fetch is dead. Remove
  it and the generator; confirm the SW registration uses `!isDevMode()`.
- **Test scenarios:**
  - App boots with no `config.json` fetch and no missing-config error.
  - No remaining references to `WEB_PRODUCTION` or `gen-config`.
- **Verification:** `rg` finds no `config.json`/`gen-config`/`WEB_PRODUCTION`
  references; app boots via transfer state only.

### U13. Config — invert `@hsm/config` to package/app-owned validation

- **Goal:** Move env validation from a central `base` to the consuming package/app;
  `base` shrinks to `ENVIRONMENT`. (R15)
- **Requirements:** R15.
- **Dependencies:** none in Track 4 (Track 1 done). Note U1/U3 added `COOKIE_*` and
  `CSRF_SECRET` to `api.ts` — those stay api-owned.
- **Files:** `packages/config/src/fields.ts` (keep the Joi rules; may split
  per-owner), `packages/config/src/base.ts` (reduce to `ENVIRONMENT`),
  `packages/config/src/api.ts` (owns `JWT_*`, `COOKIE_*`, `CSRF_SECRET`,
  `APP_BASE_URL`, `SWAGGER_*`, `SMTP_WEBHOOK_KEY`, `DEFAULT_ADMIN_*`),
  `packages/config/src/worker.ts` (owns `SMTP_*`), new per-package config surfaces
  for `@hsm/database` (`DB_POSTGRES_*`), `@hsm/queue` (`DB_REDIS_*`),
  `@hsm/storage` (`STRG_S3_*`), plus every `envs` import site that must repoint to
  the new owner. `apps/backend/api/src/test-setup.ts` +
  `apps/backend/worker/src/test-setup.ts`.
- **Approach:** Each package exports a validated `envs` for its own slice; the
  shared packages stop importing `base` for vars they own. Retain Joi validation
  everywhere (no raw `process.env`).
- **Execution note (A4 — the safety net):** Do **not** rely on `start:dev` alone —
  Joi only validates the keys a package declares, so a var removed from a package's
  slice but still read via `envs.X` throws only when that code path runs, and
  several owned vars are lazily read (`SMTP_*` on send, `APP_BASE_URL` in
  account-recovery on password reset, the webhook key on inbound webhook). **Grep
  every `envs.<VAR>` read site and assert its owning package still declares that
  key**; add coverage for the lazy paths. Boot both api and worker (`start:dev`) as
  a secondary check.
- **Test scenarios:**
  - Covers R15. `@hsm/api` boots validating only its owned vars + the packages it
    imports; omitting a worker-only var (SMTP) does not break api boot.
  - `@hsm/worker` boots without api-only vars (JWT/COOKIE/CSRF/SWAGGER) present.
  - Each package's `envs` throws when its own required var is missing.
- **Verification:** both apps `start:dev` cleanly with least-privilege env sets;
  existing api/worker test suites stay green.

### U14. Settings — invert seed ownership + idempotent insert-only seeding

- **Goal:** `@hsm/database` keeps only the settings table + accessor; each module
  registers/seeds its own settings from its own `envs`; seeds are insert-if-absent
  + idempotent. Rename `COMS_WEBHOOK_SIGNING_KEYS` → `SMTP_WEBHOOK_KEY`. (R16)
- **Requirements:** R16.
- **Dependencies:** U13.
- **Files:** `packages/database/src/settings/setting-definitions.ts` (remove env
  reads/catalog; keep only the mechanism seam),
  `packages/database/src/settings/settings-accessor.service.ts`, a registration API
  so modules contribute seed-able settings, the api coms/webhook module + worker
  coms/email module (register their own seeds from their `envs`),
  `getWebhookSigningKeys` consumers, both `test-setup.ts`, `packages/common`
  settings DTOs if the key name is surfaced.
- **Approach:** Provide a settings-registration seam; each owning module registers
  `{ key, envValue }` from its validated `envs`. Seeding uses
  `INSERT ... ON CONFLICT (key) DO NOTHING` (insert-only, never update) so N
  replicas converge without clobbering admin live-edits. Rename the webhook key
  everywhere it is read in lockstep with the env var. (`AppSettingEntity` already
  has `@Unique(['key'])`, so the ON CONFLICT target exists.)
- **Execution note:** Data-safety sensitive — add a test proving a re-seed does
  **not** overwrite an existing (admin-edited) setting value.
- **Test scenarios:**
  - Covers R16. Re-running seed on an existing key is a no-op (admin value
    preserved).
  - Concurrent/duplicate seed of the same key inserts once.
  - `SMTP_WEBHOOK_KEY` resolves through `getWebhookSigningKeys()` after the rename
    (old name no longer referenced).
  - A module's seed reads from its own `envs`, not `@hsm/database`.
- **Verification:** api/worker boot + seed idempotently; webhook verification still
  works under the new key name; suites green.

### U15. Infisical — partition /app by consuming package/module (operator-executed)

- **Goal:** Restructure Infisical `/app` into per-owner folders with per-service
  machine identities, matching the U13/U14 ownership. (R14)
- **Requirements:** R14.
- **Dependencies:** U13, U14 (folders mirror the code ownership those units
  establish); U9/U10 for `/web`.
- **Files:** `.devcontainer/script/get-secrets-infisical.sh` (fetch the owner
  folders each service needs), compose/env wiring, the layout doc under
  `docs/solutions/`. **Live secret-store mutations are performed by a human
  operator, not `ce-work`.**
- **Approach:** Folders mirror ownership: `/shared` (`ENVIRONMENT`), `/database`,
  `/queue` (redis), `/storage`, `/api`, `/worker`, `/web`. Each service injects
  only the folders whose vars its packages/app validate. One read-scoped machine
  identity per service; dev/staging/prod stay the environment axis.
- **Execution note:** Ops/config; the plan documents the target layout + fetch-
  script wiring; a human applies the Infisical-side changes.
- **Test scenarios:** none — secret-store change; verified by each service booting
  with only its owner folders.
- **Verification:** api/worker/web each boot reading only their owner folders;
  `get-secrets-infisical.sh` produces the expected per-service `.env`.

---

## Verification Contract

- **Backend:** `pnpm --filter @hsm/api test` (Jest, colocated specs), plus
  `test:e2e` where relevant. New required config vars need dummies in
  `apps/backend/api/src/test-setup.ts` (and worker's) or the suite fails to boot.
- **Frontend:** `pnpm --filter @hsm/web test` (Vitest). SSR is proven by runtime
  smoke checks, not unit tests: `pnpm --filter @hsm/web build` emits a server
  bundle, and the running SSR server returns server-rendered HTML — anonymous route
  (U6), authenticated shell via forwarded cookie (U8), no `window/localStorage is
  not defined` errors (U7).
- **Lint/format:** `pnpm lint` / `biome check` across touched workspaces.
- **Manual gates:** server-rendered authenticated first paint with no login flash
  (Track 2); app boot with no `config.json` fetch (Track 3); per-service Infisical
  fetch (Track 4).
- **Harness note:** if tool results appear to desync from calls, re-verify each
  change via `git status`/`git diff`/`grep` and the test run before proceeding —
  the test/build output is the authoritative signal.

## Definition of Done

- **Track 2 (U6–U9).** SSR renders the authenticated shell server-side using the
  forwarded cookie (read-only probe); no browser-only code executes during server
  render; the web image runs the SSR Node server. S5 request-scoped isolation is
  tested.
- **Track 3 (U10–U12).** Config comes from transfer state off `process.env`; API
  base is host-only with per-endpoint versions (including the CSRF/refresh Track-1
  consumers); `config.json`, `gen-config.mjs`, the config app-initializer, and
  `WEB_PRODUCTION` are gone; UI version is the build-time SHA.
- **Track 4 (U13–U15).** `@hsm/config` validation is package/app-owned (`base` =
  `ENVIRONMENT` only); the settings system seeds insert-only + idempotently from
  each module's own `envs` with `COMS_WEBHOOK_SIGNING_KEYS` renamed to
  `SMTP_WEBHOOK_KEY`; Infisical `/app` is partitioned by consuming package/module
  with per-service identities. Both apps boot with least-privilege env sets. Live
  secret-store changes applied by a human operator.
- **Global.** The existing Bearer contract stays byte-for-byte unchanged;
  `pnpm lint` and the per-workspace test suites are green.

---

## Open Questions

- **[P2] Track 4 scope/independence (SC1 + SC4, carried from the parent review).**
  The parent Goal Capsule claimed "each track independently shippable," but U15
  depends on U9/U10 (`/web` folder) and the companion doc
  (`docs/solutions/tooling-decisions/config-ownership-and-settings-seed-belong-to-consumers.md`)
  describes the config-ownership + settings-seed inversion as "its own cross-cutting
  refactor." **Decide before Track 4:** keep U13–U15 bundled here, or split them
  into their own dedicated plan. (This continuation plan keeps them bundled but
  sequenced last and independently landable after Track 1.)
- **SSR↔API base URL:** the server-to-server base URL in the dev container and
  compose may differ from the browser's base URL — resolve during U8/U10.
- **Deploy topology:** SSR Node server sizing and the UI health/version endpoint
  shape — resolve before the web image ships (U9).
