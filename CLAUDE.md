# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository state — .NET rewrite in progress

This repository is being rebuilt on **.NET 10 / ASP.NET Core / EF Core / Blazor Server**,
per `docs/plans/2026-07-27-001-feat-dotnet-blazor-rewrite-plan.md`. The prior
TypeScript/Node monorepo was removed from the working tree; it remains fully
reachable at tag **`freeze/typescript-2026-07-27`**, which is the frozen
*reference specification* for the rewrite — consult it for behavior, do not
transliterate it.

- **Anchor branch:** `rewrite/dotnet`. All rewrite branches fork from and merge
  into it. It is unprotected (no ruleset matches it); `development`, `main`, and
  `release/**` all require the `pr-gate` status check.
- **Frozen API contract:** `docs/reference/2026-07-27-frozen-api-contract.openapi.json`
  (OpenAPI, 58 operations) and `docs/reference/2026-07-27-frozen-api-routes.txt`.
  Contract tests for rebuilt endpoints are written from this snapshot.
- The .NET solution (`Hsm.sln`, `src/`, `tests/`) is standing up incrementally.
  If `Hsm.sln` does not exist yet, the solution-foundation units of the plan have
  not landed — check the plan before assuming structure.
- **Conventions:** `docs/reference/dotnet-conventions.md` — one-page reference for exactly which
  project and folder a given kind of code (entity, command, query, validator, port, adapter,
  endpoint, Blazor page, UI service, each test kind) belongs in, the dependency arrows between
  projects, and the pipeline behavior order. Read it before adding a new module slice.

### Projects

Libraries, named by layer — `Hsm.Domain` (entities), `Hsm.Application` (CQRS
commands/queries/handlers, ports, the pipeline), `Hsm.Contracts` (UI service interfaces; the
client-isolation leaf — no `Hsm.*` references), `Hsm.Infrastructure` (adapters: EF Core/Npgsql,
S3, Meilisearch, Redis, job queue).

Deployables, named by the door they open onto that same core — `Hsm.Api` (REST + FHIR,
stateless), `Hsm.Web` (the staff Blazor Server shell, dispatching in-process), `Hsm.Worker` (the
durable Redis Streams job consumer + scheduled work). None of the three talks to another over
HTTP; each can be built, deployed, and restarted independently.

Four test projects — `Hsm.Tests` (unit + architecture/boundary tests), `Hsm.Contract.Tests`
(pinned to the frozen HTTP contract, exercises HTTP and never names a handler class),
`Hsm.Integration.Tests` (real Postgres/Redis/RustFS containers), `Hsm.Web.Tests` (bUnit component
tests against `.razor` pages).

## Commands (inside the dev container)

```bash
# Infra (the dev container's runServices already starts these)
docker compose -f docker/docker-compose.yaml up -d postgres redis rustfs meilisearch otel-collector

# Build / test (once Hsm.sln exists) — mirrors .github/workflows/pr-validation.yml
dotnet build Hsm.sln
dotnet test tests/Hsm.Tests && dotnet test tests/Hsm.Web.Tests   # fast, no infra (CI's unit-tests job)
dotnet test tests/Hsm.Integration.Tests                          # real Postgres/Redis/RustFS/Meilisearch
dotnet test tests/Hsm.Contract.Tests                             # same infra; needs a live Postgres too
dotnet test Hsm.sln                                              # full gate, all four test projects
dotnet format Hsm.sln --verify-no-changes                        # lint gate

# Apply the schema — an explicit step, never done on host boot
dotnet run --project src/Hsm.Api -- --migrate

# Regenerate the committed OpenAPI spec after any surface change
HSM_OPENAPI_UPDATE=1 dotnet test tests/Hsm.Api.Tests --filter OpenApiSpecTests

# Run the hosts (three doors onto one core — any subset runs without the others)
dotnet run --project src/Hsm.Web        # staff shell on :5000 (published to host)
dotnet run --project src/Hsm.Api        # REST/FHIR on :5001 (published to host)
dotnet run --project src/Hsm.Worker     # durable job consumer + scheduler, no HTTP surface
```

### Infra port map (host)

| Service | Host port |
| --------- | ----------- |
| Staff shell (Hsm.Web, run locally) | 5000 |
| REST/FHIR API (Hsm.Api, run locally) | 5001 |
| Postgres | 10004 |
| Redis | 10005 |
| RustFS (S3) | 10006 |
| RustFS console | 10007 |
| Meilisearch | 10008 |
| OTLP collector (gRPC / HTTP) | 10009 / 10010 |

Local-run walkthrough:
`docs/solutions/developer-experience/2026-07-27-dotnet-dev-container-local-run.md`.

## CI

`.github/workflows/pr-validation.yml` defines the **`pr-gate`** required status
check referenced by all three branch rulesets in `.github/rulesets/`. Keep the
job name `pr-gate` — renaming it silently un-gates every protected branch (see
`docs/solutions/tooling-decisions/2026-07-02-github-ruleset-release-train-bootstrap-deadlocks.md`).
Build/test jobs no-op green while `Hsm.sln` does not exist.

## Documented solutions

`docs/solutions/` — past bugs, best practices, and workflow patterns, organized
by category with YAML frontmatter (`module`, `tags`, `problem_type`). Entries
predating the rewrite are TypeScript/NestJS/TypeORM specific: they are
historical record, still valuable for *behavioral* questions (what the frozen
system did and why), but their code references point at the frozen tag, not the
working tree.

## Oracle database constraint

The Oracle database (`DB_ORACLE_*`) is the **production legacy system**. You may only issue `SELECT` queries against it. **Never issue `DELETE`, `DROP`, `ALTER`, `CREATE`, or any DDL/destructive statement** against Oracle. No schema changes, no new tables, no migrations targeting Oracle.

The rewritten application has **no runtime Oracle connection** (PostgreSQL
only) — but the legacy system remains in production and the constraint applies
to any tooling or session that reaches it.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **hsm-app** (4746 symbols, 9988 relationships, 288 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

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
