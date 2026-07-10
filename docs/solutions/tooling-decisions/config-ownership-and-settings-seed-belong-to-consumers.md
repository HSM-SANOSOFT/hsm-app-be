---
title: Env config and settings seeds belong to the consuming package/app, not a central base
date: 2026-07-10
category: tooling-decisions
module: "@hsm/config, @hsm/database (settings), apps/backend/api, apps/backend/worker"
problem_type: best_practice
component: configuration
severity: medium
applies_when:
  - Deciding which service/package an env var belongs to (and which Infisical folder)
  - Adding a new env var and unsure whether it goes in base, api, or worker config
  - Touching the DB-backed settings system (setting-definitions.ts / SettingsAccessor)
  - Restructuring Infisical /app secrets by consuming service (SSR migration Track 4)
---

# Env config and settings seeds belong to the consuming package/app

## Context

While restructuring Infisical secrets by consuming service (part of the Angular
SSR / dual-transport auth migration, `docs/plans/2026-07-10-001-feat-angular-ssr-auth-migration-plan.md`),
we tried to give each service a least-privilege set of env vars. It didn't work
cleanly: vars that are *functionally* used by only one service — `SMTP_*`
(worker only), `SWAGGER_SITE_TITLE` (api only), the coms webhook key (api only),
`APP_BASE_URL` (api only) — could not be scoped to that service without breaking
the *other* service's boot with `Config validation error: X is required`.

## Guidance

**Env config ownership should live with the consumer** — the package or app that
actually reads the var — not in a central `base` that every service imports.

- `@hsm/config` today centralizes validation: `base.ts` validates one fixed
  `BASE_KEYS` set, and **both** `api` and `worker` import `base` (transitively,
  via the shared packages `@hsm/database`/`@hsm/queue`/`@hsm/storage`). So every
  var in `base.required()` must be injected into *both* services regardless of
  who uses it. That is the artifact that forces false "shared" vars.
- The target model is **package/module-owned config**: each package validates
  its own slice and the app composes what it imports —
  `@hsm/database` → `DB_POSTGRES_*`, `@hsm/queue` → `DB_REDIS_*` (redis is
  BullMQ's, not the DB's), `@hsm/storage` → `STRG_S3_*`, worker's coms/email
  module → `SMTP_*`, api's coms/webhook module → the webhook key, the api app →
  `JWT_*`/`COOKIE_*`/`APP_BASE_URL`/`SWAGGER_*`/`DEFAULT_ADMIN_*`. `base` shrinks
  to just `ENVIRONMENT`. Validation stays in `envs` (Joi) — the win is *where*
  it lives, not dropping it.

**The DB-backed settings seed is the coupling that blocks this.**
`packages/database/src/settings/setting-definitions.ts` (in `@hsm/database`,
loaded by both apps) reads `envs.SMTP_*`, `envs.SWAGGER_SITE_TITLE`, and the
webhook key as *seed/fallback values* for live-editable DB settings. Because that
low-level package imports `base` envs, it drags all those vars into `base`. A
low-level package should not know about SMTP or Swagger. The fix is to **invert
seeding: each module registers its own seed-able settings** (from its own
validated `envs`) with the settings system; `@hsm/database` keeps only the
settings *table + accessor* (mechanism), no env reads and no catalog.

**Two hard rules the seed system must honor** (surfaced by "what happens with N
app instances?"):

1. **Seed is insert-if-absent, never update.** If seeding *updates* on boot,
   every replica restart/deploy silently overwrites an admin's live-edited
   setting with the env value. Seed = initial value only.
2. **Seeding must be idempotent** — `INSERT ... ON CONFLICT (key) DO NOTHING` —
   so N replicas racing at boot converge to one value with no duplicates and no
   clobbering. To avoid even N redundant no-op writes, move seeding to a one-shot
   init/migration or leader-only step; idempotent-on-boot is the acceptable
   baseline.

## Why This Matters

- **Least-privilege secrets**: a service should only receive the secrets it uses.
  Central `base` validation defeats this and forces credentials (e.g. SMTP) into
  services that never touch them.
- **Layering**: a low-level package (`@hsm/database`) reading app-level env
  (SMTP, Swagger) is an inversion that will keep re-surfacing as friction.
- **Data safety**: the insert-only + idempotent seed rules prevent a deploy from
  wiping admin-configured settings across a horizontally-scaled deployment.

## When to Apply

- Any time you add an env var: put its validation in the package/app that reads
  it, and its Infisical folder mirrors that owner — not a catch-all `/shared`.
- Before extending the settings system: register the seed from the owning
  module, keep `@hsm/database` to mechanism only, and make the seed
  insert-if-absent + idempotent.

## Examples

Actual consumers verified by grep (2026-07-10):

| Var | Real consumer | Was in `base` because |
|-----|---------------|-----------------------|
| `SMTP_*` | worker `coms/email/smtp-transport.provider.ts` | settings seed in `@hsm/database` |
| `SWAGGER_SITE_TITLE` | api `main.ts` (Swagger) | settings seed in `@hsm/database` |
| `COMS_WEBHOOK_SIGNING_KEYS` | api `coms/webhook/coms-webhook.service.ts` (verifies inbound Mandrill delivery-status webhooks) | settings seed in `@hsm/database` |
| `APP_BASE_URL` | api `main.ts` (CORS) + `account-recovery.service.ts` (reset link) | in `API_KEYS` already |

Naming note: this repo is renaming the webhook key to `SMTP_WEBHOOK_KEY`; the
code that reads it (`getWebhookSigningKeys()`, `setting-definitions.ts`, both
`test-setup.ts`) must be updated to the new name in lockstep — the env name and
the code that reads it must always agree.

This config-ownership inversion + settings-seed redesign is tracked as its own
cross-cutting refactor (beyond the SSR plan's Track 3/4 as originally written).
