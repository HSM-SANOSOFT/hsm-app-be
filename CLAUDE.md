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

## Commands (inside the dev container)

```bash
# Infra (the dev container's runServices already starts these)
docker compose -f docker/docker-compose.yaml up -d postgres redis rustfs meilisearch otel-collector

# Build / test (once Hsm.sln exists)
dotnet build Hsm.sln
dotnet test Hsm.sln --filter "FullyQualifiedName!~Integration"   # unit only
dotnet test tests/Hsm.Integration.Tests                          # real infra
dotnet format Hsm.sln --verify-no-changes                        # lint gate

# Run the app host
dotnet run --project src/Hsm.Web        # app on :5000 (published to host)
```

### Infra port map (host)

| Service | Host port |
| --------- | ----------- |
| App (Hsm.Web, run locally) | 5000 |
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

The Oracle database (`DB_ORACLE_*`) is the **production legacy system**. You may only issue `SELECT` or `UPDATE` queries against it. **Never issue `DELETE`, `DROP`, `ALTER`, `CREATE`, or any DDL/destructive statement** against Oracle. No schema changes, no new tables, no migrations targeting Oracle.

The rewritten application has **no runtime Oracle connection** (PostgreSQL
only) — but the legacy system remains in production and the constraint applies
to any tooling or session that reaches it.
