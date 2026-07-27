FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

RUN apt-get update && apt-get install -y --no-install-recommends \
    bash \
    git \
    curl \
    unzip \
    ca-certificates \
    gnupg \
  && rm -rf /var/lib/apt/lists/*

# Infisical CLI — https://infisical.com/docs/cli/overview#debianubuntu
# Pinned: 0.43.x switched to the /api/v4 secrets routes, which the self-hosted
# server (vault.hospitalsm.org) does not serve — export fails with 404.
RUN curl -1sLf 'https://artifacts-cli.infisical.com/setup.deb.sh' | bash \
  && apt-get update \
  && apt-get install -y --no-install-recommends infisical=0.42.6 \
  && rm -rf /var/lib/apt/lists/*

ARG USER_UID=1000
ARG USER_GID=1000

RUN if getent group ${USER_GID} >/dev/null 2>&1; then \
      groupmod -n vscode $(getent group ${USER_GID} | cut -d: -f1); \
    else \
      groupadd --gid ${USER_GID} vscode; \
    fi \
  && if getent passwd ${USER_UID} >/dev/null 2>&1; then \
      usermod -l vscode -d /home/vscode -m $(getent passwd ${USER_UID} | cut -d: -f1); \
    else \
      useradd --uid ${USER_UID} --gid ${USER_GID} -m --shell /bin/bash vscode; \
    fi

ENV PATH="/home/vscode/.local/bin:${PATH}" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /workspace

RUN mkdir -p /home/vscode/.claude && chown -R vscode:vscode /home/vscode/.claude /workspace
