#!/usr/bin/env bash

set -u

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
SUITE_NAMES=()
SUITE_STATUSES=()

run_suite() {
  local name="$1"
  local directory="$2"
  shift 2

  SUITE_NAMES+=("$name")
  printf '\n===== %s =====\n' "$name"

  if (cd "$directory" && "$@"); then
    SUITE_STATUSES+=(0)
    printf '\n===== %s: PASSOU =====\n' "$name"
  else
    local status=$?
    SUITE_STATUSES+=("$status")
    printf '\n===== %s: FALHOU (código %s) =====\n' "$name" "$status"
  fi
}

run_suite "Frontend (Angular)" "$ROOT_DIR/apps/web" npm test -- --watch=false
run_suite "API (.NET)" "$ROOT_DIR" dotnet test apps/api.tests/AudioApi.Tests.csproj
run_suite "Worker de áudio (pytest)" "$ROOT_DIR" uv run --directory apps/workers/audio pytest
run_suite "Contrato OpenAPI" "$ROOT_DIR" uv run --project apps/workers/audio \
  openapi-spec-validator specs/002-youtube-audio-extraction/contracts/audio-extractions.openapi.yaml

printf '\n===== Resumo dos testes =====\n'
failed=0
for index in "${!SUITE_NAMES[@]}"; do
  if [ "${SUITE_STATUSES[$index]}" -eq 0 ]; then
    printf 'PASSOU  %s\n' "${SUITE_NAMES[$index]}"
  else
    printf 'FALHOU  %s (código %s)\n' \
      "${SUITE_NAMES[$index]}" "${SUITE_STATUSES[$index]}"
    failed=1
  fi
done

if [ "$failed" -ne 0 ]; then
  exit 1
fi
