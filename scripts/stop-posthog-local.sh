#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
POSTHOG_DIR="$ROOT_DIR/.local/posthog"

if [ ! -f "$POSTHOG_DIR/.env" ]; then
  printf 'PostHog local ainda não foi configurado.\n'
  exit 0
fi

docker compose \
  --project-directory "$POSTHOG_DIR" \
  --env-file "$POSTHOG_DIR/.env" \
  --env-file "$POSTHOG_DIR/.env.services" \
  --project-name taskpilot-posthog \
  -f "$POSTHOG_DIR/docker-compose.hobby.yml" \
  -f "$ROOT_DIR/docker-compose.posthog.yml" \
  down
