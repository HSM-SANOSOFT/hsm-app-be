#!/bin/sh
set -eu

echo "Logging in to Infisical and fetching secrets..."

INFISICAL_TOKEN=$(infisical login \
  --method=universal-auth \
  --client-id="$INFISICAL_MACHINE_CLIENT_ID" \
  --client-secret="$INFISICAL_MACHINE_CLIENT_SECRET" \
  --plain --silent)

SECRETS_DOTENV=$(infisical export \
  --token "$INFISICAL_TOKEN" \
  --projectId "$PROJECT_ID" \
  --domain "$INFISICAL_API_URL" \
  --path "$SECRET_PATH" \
  --env dev \
  --format dotenv)

WORKSPACE_DIR="$(cd "$(dirname "$0")/../.." && pwd)"

# One dotenv-format file at the repo root (gitignored). The root .env is NOT
# used — it holds the Infisical *bootstrap* creds the workspace container
# loads via compose env_file; overwriting it would clobber them. The .NET
# hosts load secrets.env at startup in Development.
printf '%s\n' "$SECRETS_DOTENV" > "$WORKSPACE_DIR/secrets.env"
echo "Wrote $WORKSPACE_DIR/secrets.env"

echo "Restart your dev server to pick up changes."
