# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Per-workspace context

Each app and package has its own `CLAUDE.md`. **Always read it before working in that directory** — it covers commands, conventions, module wiring, and gotchas.

| Workspace | File |
| --------- | ---- |
| `@hsm/api` | [`apps/backend/api/CLAUDE.md`](apps/backend/api/CLAUDE.md) |
| `@hsm/worker` | [`apps/backend/worker/CLAUDE.md`](apps/backend/worker/CLAUDE.md) |
| `@hsm/web` | [`apps/frontend/web/CLAUDE.md`](apps/frontend/web/CLAUDE.md) |
| `@hsm/common` | [`packages/common/CLAUDE.md`](packages/common/CLAUDE.md) |
| `@hsm/config` | [`packages/config/CLAUDE.md`](packages/config/CLAUDE.md) |
| `@hsm/database` | [`packages/database/CLAUDE.md`](packages/database/CLAUDE.md) |
| `@hsm/queue` | [`packages/queue/CLAUDE.md`](packages/queue/CLAUDE.md) |
| `@hsm/storage` | [`packages/storage/CLAUDE.md`](packages/storage/CLAUDE.md) |

## Monorepo commands (run from repo root, inside container)

```bash
# Start full stack (api + worker + postgres + redis + rustfs)
docker compose -f docker/docker-compose.yaml up

# Start only infra (no app containers)
docker compose -f docker/docker-compose.yaml up postgres redis rustfs

# Exec into running containers
docker exec -it hsm-app-be-api sh
docker exec -it hsm-app-be-worker sh

# Install deps (regenerates pnpm-lock.yaml)
pnpm install

# Lint / format
pnpm lint
pnpm lint:fix
pnpm check:fix   # lint + format together

# Build everything via Turborepo
pnpm build
```

### Port map

| Service | Host port |
| --------- | ----------- |
| API | 10001 |
| Worker | 10002 |
| Postgres | 10003 |
| Redis | 10004 |
| RustFS (S3) | 10006 |
| RustFS console | 10007 |
| RedisInsight | 10007 |
| pgAdmin | 10008 |

## Run & test locally (inside the dev container)

The everyday dev loop does **not** use the app images. Only the external
services run as compose containers; `@hsm/api`, `@hsm/worker`, and `@hsm/web`
run directly inside the dev container via pnpm:

```bash
# Infra only (the dev container's runServices already starts these)
docker compose -f docker/docker-compose.yaml up -d postgres redis rustfs

# Apps — run locally, each in its own terminal
pnpm --filter @hsm/api start:dev     # API on :4201  (wait for "Seeded default admin user")
pnpm --filter @hsm/worker start:dev
pnpm --filter @hsm/web dev           # Angular dev server on :4200
```

The dev container forwards **3000** (API) and **4200** (web) to the host, so the
browser reaches the API at `localhost:4201` and the app at `localhost:4200`.
**The Port map above is the `docker compose up` (full-stack) mapping, not the
local-run model** — in local-run the frontend dev env
(`apps/frontend/web/src/environments/environment.development.ts`) targets
`http://localhost:4201/v1`.

**Log in** with the seeded default admin: username `admin` (username-based — not
the email) plus the `DEFAULT_ADMIN_PASSWORD` value from `apps/backend/api/.env`.
After running `.devcontainer/script/get-secrets-infisical.sh`, **restart
`start:dev`** so dotenv reloads the new `.env`. Full walkthrough:
`docs/solutions/developer-experience/2026-06-25-dev-container-local-run-and-login.md`.

## Documented solutions

`docs/solutions/` — past bugs, best practices, and workflow patterns, organized by category with YAML frontmatter (`module`, `tags`, `problem_type`). Relevant when debugging, adding entities, or working in an area that may have prior incidents.

## Oracle database constraint

The Oracle database (`DB_ORACLE_*`) is the **production legacy system**. You may only issue `SELECT` or `UPDATE` queries against it. **Never issue `DELETE`, `DROP`, `ALTER`, `CREATE`, or any DDL/destructive statement** against Oracle. No schema changes, no new tables, no migrations targeting Oracle.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **hsm-app** (5011 symbols, 10640 relationships, 194 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "development"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/hsm-app/context` | Codebase overview, check index freshness |
| `gitnexus://repo/hsm-app/clusters` | All functional areas |
| `gitnexus://repo/hsm-app/processes` | All execution flows |
| `gitnexus://repo/hsm-app/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
