#!/bin/sh
set -eu

echo "Logging in to Infisical and fetching secrets..."
echo "INFISICAL_MACHINE_CLIENT_ID: $INFISICAL_MACHINE_CLIENT_ID"
echo "INFISICAL_API_URL: $INFISICAL_API_URL"
echo "PROJECT_ID: $PROJECT_ID"
echo "SECRET_PATH: $SECRET_PATH"


INFISICAL_TOKEN=$(infisical login \
  --method=universal-auth \
  --client-id="$INFISICAL_MACHINE_CLIENT_ID" \
  --client-secret="$INFISICAL_MACHINE_CLIENT_SECRET" \
  --plain --silent)

infisical export \
  --token "$INFISICAL_TOKEN" \
  --projectId "$PROJECT_ID" \
  --domain "$INFISICAL_API_URL" \
  --path "$SECRET_PATH" \
  --env dev \
  --format dotenv \
  