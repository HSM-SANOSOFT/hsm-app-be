#!/bin/sh
set -eu
export INFISICAL_TOKEN=$(infisical login --method=universal-auth --client-id=$INFISICAL_MACHINE_CLIENT_ID --client-secret=$INFISICAL_MACHINE_CLIENT_SECRET --plain --silent)
exec infisical run --token $INFISICAL_TOKEN --projectId $PROJECT_ID --domain $INFISICAL_API_URL --path $SECRET_PATH --env dev -- "$@"