# Hsm.Worker — background job consumer, no HTTP surface (plan U17: three
# independently deployable hosts, one core). Multi-stage: the SDK image
# restores and publishes; the plain runtime image (no ASP.NET) serves. Build
# from the repo root:
#   docker build -f docker/worker.Dockerfile .
# (docker/docker-compose.yaml does exactly this for the hsm-app-worker service.)

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Every project's *.csproj plus the shared MSBuild props, copied before any
# source file, so `dotnet restore` is its own Docker layer: it only re-runs
# when a dependency changes, never on a source edit. The set is identical
# across api/web/worker.Dockerfile on purpose — the layer is byte-for-byte
# reusable across all three image builds.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Hsm.Domain/Hsm.Domain.csproj src/Hsm.Domain/
COPY src/Hsm.Application/Hsm.Application.csproj src/Hsm.Application/
COPY src/Hsm.Contracts/Hsm.Contracts.csproj src/Hsm.Contracts/
COPY src/Hsm.Infrastructure/Hsm.Infrastructure.csproj src/Hsm.Infrastructure/
COPY src/Hsm.Api/Hsm.Api.csproj src/Hsm.Api/
COPY src/Hsm.Web/Hsm.Web.csproj src/Hsm.Web/
COPY src/Hsm.Worker/Hsm.Worker.csproj src/Hsm.Worker/
RUN dotnet restore src/Hsm.Worker/Hsm.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Hsm.Worker/Hsm.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# aspnet, not runtime: Hsm.Infrastructure takes the Microsoft.AspNetCore.App
# framework reference for ASP.NET Core Identity (see Hsm.Infrastructure.csproj),
# so every host that composes it needs that shared framework present — this one
# included, even though it opens no HTTP surface of its own.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

# runtime:10.0-noble (Ubuntu, not the -chiseled variant) ships no unprivileged
# user by default — create one and run as it.
RUN groupadd --gid 10001 hsmapp \
    && useradd --uid 10001 --gid hsmapp --create-home --shell /usr/sbin/nologin hsmapp

ENV DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build --chown=hsmapp:hsmapp /app/publish .

USER hsmapp

# THE queue consumer (Hsm.Api/Hsm.Web only ever enqueue). No port: this host
# answers no requests, so an api/web restart or redeploy cannot strand a job —
# what it enqueued is in the Redis Streams keys this host reads.
ENTRYPOINT ["dotnet", "Hsm.Worker.dll"]
