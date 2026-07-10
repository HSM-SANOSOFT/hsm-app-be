---
title: Frontend rendering architecture — Angular SSR (decision)
date: 2026-07-08
status: decided → ready for planning
module: apps/frontend/web
tags: [architecture, frontend, ssr, config, angular, auth, infisical]
problem_type: architecture-decision
---

# Frontend rendering architecture: Angular SSR

> **Status:** DECIDED. This supersedes the earlier "open input" version of this
> doc. The decision and its rationale are recorded below; the four work tracks
> are the input for a `ce-plan` implementation plan. Auth migration (Track 1) is
> the prerequisite the rest depends on.

## Decision

Adopt **server-side rendering with Angular's first-party `@angular/ssr`**, keep
Angular (no framework switch), and migrate session auth to a **dual-transport**
model (`httpOnly` cookie for the browser/SSR, Bearer token retained for
integrations). Retire the runtime `config.json` machinery in favour of
server-read `process.env` + transfer state. Restructure Infisical secrets by
consuming service.

## Why (how we got here)

While making `@hsm/web` config env-driven we built a runtime `config.json`
(generated from env at container start, fetched at boot) + an app-initializer.
It worked but felt like fighting the platform. The deeper realization: the
friction is a symptom of a **client-side design** — config, versioning, and
compute all have to be shipped/fetched to a device we don't control.

Two facts turned the lean into a decision:

- **Hospital hardware is low-end.** The heavy lifting should happen on
  infrastructure we can size and standardize, not on the client. SSR renders the
  first paint on the server (fast even on weak CPUs); Angular 21 **incremental
  hydration** defers/limits client-side JS execution.
- **One Docker image, env at runtime.** The SSR server is a Node process — it
  reads `process.env` natively (like the NestJS apps), so the *same* image runs
  in every environment by changing env only, no rebuild. Build-time approaches
  (`environment.ts`, an `.env`-baking plugin) freeze the API URL into the bundle
  and force a rebuild per environment.

### Corrections to earlier assumptions (recorded so they don't recur)

- **"Baked envs are public — use an `.env` plugin"** — a build-time `.env`
  plugin (`@ngx-env/builder`, `import.meta.env.NG_APP_*`) is **exactly as public**
  as `environment.ts`; both become literal strings in the browser bundle. It
  changes DX, not exposure. Moot anyway: `apiBaseUrl`/version/production are not
  secrets. SSR is what actually keeps config off the client (server-side + only
  non-secret bits sent via transfer state).
- **"SSR moves all compute to the server"** — it does **not**. After hydration
  the app still runs client-side JS; SSR moves the *first render* + lets you
  defer hydration. Full server-only compute would be a thin-client/HTMX rewrite
  (rejected — loses the Angular app). SSR + incremental hydration is the
  pragmatic sweet spot for low-end devices.
- **Framework switch (Next.js/RSC)** — rejected. It would rewrite the entire
  working app (zoneless Angular 21, PrimeNG, Monaco, Transloco, typed API client,
  auth/guards/nav, features U6–U15) in React and break the Angular↔NestJS
  symmetry. `@angular/ssr` is built-in and covers the goal.

## The gating constraint: auth

The current app stores access + refresh **JWTs in `localStorage`**
(`core/auth/token-storage.ts`); every route except `/login` is behind
`authGuard`. **localStorage is never attached to the document request**, so an
SSR server cannot know who the user is at render time — it would render the
anonymous state and the client would re-render after hydration (a login flash +
throwaway server render on every page). That would make SSR *worse* than the
current SPA.

Therefore SSR's benefit is **gated on** moving the browser session to an
`httpOnly` cookie that rides the document request. This is Track 1 and the whole
rest depends on it.

---

## Track 1 — Auth migration (prerequisite)

Dual-transport auth: **Bearer retained for integrations, `httpOnly` cookie added
for browser/SSR.** Additive — the API's existing token contract is untouched.

- **Backend — token issuance unchanged.** `/auth/login` still returns
  `{ access_token, refresh_token }` in the body (integrations / direct-API
  clients keep working).
- **Backend — also set cookies.** The same login response sets `httpOnly`
  cookies (access + refresh); `/auth/refresh` reads/rotates the refresh cookie;
  `/auth/logout` clears them. Cookie attributes: `httpOnly`, `secure`,
  `SameSite=Strict`, refresh cookie path-scoped to the refresh route.
- **Backend — JWT guard extracts from either source:**
  ```ts
  jwtFromRequest: ExtractJwt.fromExtractors([
    ExtractJwt.fromAuthHeaderAsBearerToken(),      // integrations
    (req) => req?.cookies?.access_token ?? null,   // browser / SSR
  ])
  ```
- **Frontend — drop localStorage.** Remove `TokenStorage`/localStorage token
  handling; the interceptor stops attaching Bearer from JS (the cookie is sent
  automatically). `restoreSession`/guards derive from the cookie-backed session.
- **CSRF.** Cookies auto-send, so add CSRF protection: `SameSite=Strict` +
  a CSRF token on mutations. (Net security win: `httpOnly` tokens can't be
  stolen by XSS, unlike the current localStorage tokens.)
- **SSR server.** Forwards the incoming cookie to the API on server-side data
  calls; refresh handled server-side; guards run server-side off the cookie.

## Track 2 — SSR enablement

- Add **`@angular/ssr`** (Angular 21; zoneless SSR is supported). Server routes +
  incremental hydration for the low-end-hardware win.
- **Platform-guard browser-only code** (`isPlatformBrowser` / `afterNextRender`):
  **Monaco** (editor, KTD1), **Handlebars** client preview, `localStorage` for
  lang/last-username, and the **Transloco** loader `fetch('/i18n/..')` (needs an
  absolute URL server-side).
- Web image entrypoint runs the SSR Node server instead of a static file server.

## Track 3 — Config via transfer state

Retire `config.json` + `scripts/gen-config.mjs` + the boot app-initializer. The
SSR server reads `process.env` and passes non-secret config to the client via
Angular transfer state. Apply the pending corrections:

- **API base URL = host only** (e.g. `http://localhost:4201`); **version is
  per-endpoint** (`/v1`, `/v2` chosen per call — the app may hit multiple API
  versions). The old `config.json` baked `/v1` into the base — wrong.
- **UI version from a build-time file/endpoint** (git SHA), symmetric with the
  API's `/v1/health/version` — **not** a `WEB_APP_VERSION` env var.
- **Drop `WEB_PRODUCTION`** (unused — the SW toggle uses `!isDevMode()`).

## Track 4 — Infisical restructure

Partition `/app` by **consuming service**, least-privilege machine identity per
service, mirroring the `@hsm/config` per-app split (`fields.ts` shared,
`api.ts`/`worker.ts` pick their own vars).

```
/app
  /shared    # vars used by >1 service (DB_POSTGRES_*, DB_REDIS_*, …)
  /api       # api-only: JWT_*, SMTP_*, STRG_S3_*, cookie settings…
  /worker    # worker-only
  /web       # NEW: the SSR server's env — WEB_API_BASE_URL (host only), PORT
```

- **Shared secrets defined once in `/shared`**, referenced from `/api`/`/worker`
  via Infisical secret references (`${shared.DB_POSTGRES_HOST}`) — no
  duplication/drift.
- **Environments (dev/staging/prod) are the orthogonal Infisical *environment*
  axis**, not folders — same tree across all three.
- **One machine identity per service**, read-scoped to its folder + `/shared`.
- SSR makes `/web` a real env consumer for the first time (the SSR Node server
  has `process.env`), finally symmetric with `/api`/`/worker`.
- Don't over-shard past service boundaries (no per-feature folders).

---

## Requirements / constraints (carried, still binding)

- Infrastructure drives config/resources; client stays as thin as SSR allows.
  Predictable across varied low-end client devices.
- Keep Angular's structure + NestJS similarity.
- API base URL = host only; version per-endpoint.
- UI version from build-time file, not env.
- `WEB_PRODUCTION` dropped.

## Open questions for the plan

- Exact cookie lifetimes / rotation for access vs refresh; refresh-cookie path
  scope.
- CSRF mechanism specifics (double-submit token vs framework helper) for the
  Nest API.
- SSR ↔ API networking in the dev container and in compose (server-to-server base
  URL vs the browser's base URL — they may differ).
- Deploy topology: SSR Node server sizing; health/version endpoint for the UI.
- Migration order: land Track 1 (auth) behind the existing SPA first, or in
  lockstep with SSR enablement.

## Current state of the config work (context)

- **Landed** (feature branch → development): backend `@hsm/config` per-app split
  (`@hsm/config` = base, `/api`, `/worker`) and the frontend runtime `config.json`
  + `ConfigService` + Transloco i18n. Tested: api 391, worker 64, web 264.
- **Discarded uncommitted:** a half-done rework (apiBaseUrl → host-only,
  version-per-endpoint, drop `WEB_PRODUCTION`, `version.json`) — superseded by
  this decision. Its *intent* is folded into Track 3.
- **`config.json` + `gen-config.mjs` + the boot app-initializer are slated for
  removal in Track 3** once SSR transfer state replaces them.
