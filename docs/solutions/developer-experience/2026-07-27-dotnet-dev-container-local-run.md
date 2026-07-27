---
module: dev-environment
tags: [devcontainer, dotnet, local-run, infisical, compose]
problem_type: workflow
---

# Dev container local run — .NET stack

Replaces `2026-06-25-dev-container-local-run-and-login.md`, which described the
Node/pnpm loop removed by the rewrite (tag `freeze/typescript-2026-07-27`).

## Model

Only infrastructure runs as compose services. The application hosts run
directly inside the dev container with the .NET CLI.

```bash
# Bring the container + infra up (CLI-driven repo; VS Code also works)
devcontainer up --workspace-folder .

# runServices starts: postgres, redis, rustfs, meilisearch, otel-collector
# post-create: dotnet restore (skipped while Hsm.sln doesn't exist)
# post-start:  .devcontainer/script/get-secrets-infisical.sh → writes ./secrets.env
```

Inside the container:

```bash
dotnet build Hsm.sln
dotnet run --project src/Hsm.Web       # app on http://localhost:5000 (published to host)
dotnet run --project src/Hsm.Worker    # background host, when needed
```

## Secrets

The root `.env` holds only the Infisical *bootstrap* machine credentials
(loaded by the workspace service via compose `env_file`). Application secrets
are exported by `post-start` into `./secrets.env` (gitignored). Do not
overwrite `.env` with app secrets — that clobbers the bootstrap credentials.

After re-fetching secrets, restart the running `dotnet` process so it re-reads
configuration.

## Ports (host side)

| Service | Port |
| --- | --- |
| App (Hsm.Web) | 5000 |
| Postgres | 10004 |
| Redis | 10005 |
| RustFS S3 / console | 10006 / 10007 |
| Meilisearch | 10008 |
| OTLP gRPC / HTTP | 10009 / 10010 |

The collector being down never blocks app startup — telemetry export is not a
boot dependency.

## Gotchas

- The devcontainer base is `mcr.microsoft.com/dotnet/sdk:10.0-noble` (Ubuntu).
  .NET 10 publishes no Debian `bookworm` SDK tags — don't "fix" the base back.
- CI's integration job provisions the same infra set with the same credentials
  (`.github/workflows/pr-validation.yml`); if auth works in CI but not locally,
  compare against `docker/docker-compose.yaml` mounts.
