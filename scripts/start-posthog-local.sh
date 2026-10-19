#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
POSTHOG_DIR="$ROOT_DIR/.local/posthog"
POSTHOG_SOURCE="$POSTHOG_DIR/posthog"

if ! command -v docker >/dev/null 2>&1 || ! docker compose version >/dev/null 2>&1; then
  printf 'Erro: Docker Compose v2 é necessário para iniciar o PostHog local.\n' >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  printf 'Erro: inicie o Docker Desktop e execute este script novamente.\n' >&2
  exit 1
fi

if [ ! -d "$POSTHOG_SOURCE/.git" ]; then
  command -v git >/dev/null 2>&1 || {
    printf 'Erro: Git é necessário para baixar os arquivos oficiais do PostHog.\n' >&2
    exit 1
  }
  mkdir -p "$POSTHOG_DIR"
  git clone --depth 1 --filter=blob:none --sparse \
    https://github.com/PostHog/posthog.git "$POSTHOG_SOURCE"
fi

git -C "$POSTHOG_SOURCE" sparse-checkout set --no-cone --skip-checks \
  .env.services \
  docker-compose.base.yml \
  docker-compose.hobby.yml \
  'docker/**' \
  'posthog/idl/**' \
  'posthog/posthog/user_scripts/**'

cp "$POSTHOG_SOURCE/docker-compose.base.yml" "$POSTHOG_DIR/docker-compose.base.yml"
cp "$POSTHOG_SOURCE/docker-compose.hobby.yml" "$POSTHOG_DIR/docker-compose.hobby.yml"
cp "$POSTHOG_SOURCE/.env.services" "$POSTHOG_DIR/.env.services"
mkdir -p "$POSTHOG_DIR/clickhouse-config"
cp "$POSTHOG_SOURCE/docker/clickhouse/config.xml" "$POSTHOG_DIR/clickhouse-config/config.xml"
cp "$POSTHOG_SOURCE/docker/clickhouse/config.d/default.xml" "$POSTHOG_DIR/clickhouse-config/default.xml"
cp "$POSTHOG_SOURCE/docker/clickhouse/users.xml" "$POSTHOG_DIR/clickhouse-config/users.xml"
cp "$POSTHOG_SOURCE/docker/clickhouse/user_defined_function.xml" \
  "$POSTHOG_DIR/clickhouse-config/user_defined_function.xml"
mkdir -p "$POSTHOG_DIR/service-configs"
cp "$POSTHOG_SOURCE/docker/livestream/configs-hobby.yml" "$POSTHOG_DIR/service-configs/configs-hobby.yml"
cp "$POSTHOG_SOURCE/docker/kafka/topics.txt" "$POSTHOG_DIR/service-configs/topics.txt"
mkdir -p "$POSTHOG_DIR/clickhouse-init"
cp "$POSTHOG_SOURCE/docker/clickhouse/docker-entrypoint-initdb.d/init-db.sh" \
  "$POSTHOG_DIR/clickhouse-init/init-db.sh"
chmod +x "$POSTHOG_DIR/clickhouse-init/init-db.sh"
python3 - "$POSTHOG_DIR/docker-compose.hobby.yml" <<'PY'
from pathlib import Path
import sys

compose_path = Path(sys.argv[1])
contents = compose_path.read_text()
contents = contents.replace("./posthog/docker/clickhouse/config.xml", "./clickhouse-config/config.xml")
contents = contents.replace("./posthog/docker/clickhouse/config.d/default.xml", "./clickhouse-config/default.xml")
contents = contents.replace("./posthog/docker/clickhouse/users.xml", "./clickhouse-config/users.xml")
contents = contents.replace(
    "./posthog/docker/clickhouse/user_defined_function.xml",
    "./clickhouse-config/user_defined_function.xml",
)
contents = contents.replace(
    "./posthog/docker/livestream/configs-hobby.yml",
    "./service-configs/configs-hobby.yml",
)
contents = contents.replace("./posthog/docker/kafka/topics.txt", "./service-configs/topics.txt")
contents = contents.replace(
    "./posthog/docker/clickhouse/docker-entrypoint-initdb.d",
    "./clickhouse-init",
)
compose_path.write_text(contents)
PY

mkdir -p "$POSTHOG_DIR/compose" "$POSTHOG_DIR/share"
cat >"$POSTHOG_DIR/compose/start" <<'EOF'
#!/bin/bash
set -e
/compose/wait
./bin/migrate
./bin/docker-server
EOF
cat >"$POSTHOG_DIR/compose/temporal-django-worker" <<'EOF'
#!/bin/bash
./bin/temporal-django-worker
EOF
cat >"$POSTHOG_DIR/compose/wait" <<'EOF'
#!/usr/bin/env python3
import socket
import time

for attempt in range(120):
    try:
        for host, port in (("clickhouse", 9000), ("db", 5432)):
            with socket.create_connection((host, port), timeout=2):
                pass
        break
    except OSError:
        time.sleep(5)
else:
    raise SystemExit("Timed out waiting for Postgres and ClickHouse")
EOF
chmod +x "$POSTHOG_DIR/compose/start" "$POSTHOG_DIR/compose/temporal-django-worker" "$POSTHOG_DIR/compose/wait"

if [ ! -f "$POSTHOG_DIR/share/GeoLite2-City.mmdb" ]; then
  if command -v brotli >/dev/null 2>&1; then
    curl -fsSL https://mmdbcdn.posthog.net/ --http1.1 | brotli --decompress \
      >"$POSTHOG_DIR/share/GeoLite2-City.mmdb"
  elif python3 -c 'import brotli' >/dev/null 2>&1; then
    curl -fsSL https://mmdbcdn.posthog.net/ --http1.1 | python3 -c \
      'import brotli, sys; sys.stdout.buffer.write(brotli.decompress(sys.stdin.buffer.read()))' \
      >"$POSTHOG_DIR/share/GeoLite2-City.mmdb"
  else
    printf 'Erro: instale brotli para preparar a base GeoIP do PostHog (brew install brotli).\n' >&2
    exit 1
  fi
  chmod 644 "$POSTHOG_DIR/share/GeoLite2-City.mmdb"
  printf '{"date":"%s"}\n' "$(date +%Y-%m-%d)" >"$POSTHOG_DIR/share/GeoLite2-City.json"
fi

if [ ! -f "$POSTHOG_DIR/.env" ]; then
  command -v openssl >/dev/null 2>&1 || {
    printf 'Erro: OpenSSL é necessário para gerar os segredos locais do PostHog.\n' >&2
    exit 1
  }
  cat >"$POSTHOG_DIR/.env" <<EOF
POSTHOG_SECRET=$(openssl rand -hex 32)
ENCRYPTION_SALT_KEYS=$(openssl rand -hex 16)
BROWSERLESS_SECRET=$(openssl rand -hex 32)
INTERNAL_REQUEST_TOKEN=$(openssl rand -hex 32)
DOMAIN=localhost
TLS_BLOCK=
CADDY_TLS_BLOCK=
CADDY_HOST=http://localhost
REGISTRY_URL=posthog/posthog
POSTHOG_APP_TAG=latest
POSTHOG_NODE_TAG=latest
EOF
  chmod 600 "$POSTHOG_DIR/.env"
fi

# A previous interrupted setup may leave placeholder or malformed secrets behind.
if ! python3 - "$POSTHOG_DIR/.env" <<'PY'
from pathlib import Path
import sys

values = {}
for line in Path(sys.argv[1]).read_text().splitlines():
    if "=" in line and not line.lstrip().startswith("#"):
        key, value = line.split("=", 1)
        values[key] = value
raise SystemExit(0 if len(values.get("ENCRYPTION_SALT_KEYS", "")) == 32 else 1)
PY
then
  command -v openssl >/dev/null 2>&1 || {
    printf 'Erro: OpenSSL é necessário para corrigir o segredo local do PostHog.\n' >&2
    exit 1
  }
  python3 - "$POSTHOG_DIR/.env" "$(openssl rand -hex 16)" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
lines = path.read_text().splitlines()
key, value = "ENCRYPTION_SALT_KEYS", sys.argv[2]
for index, line in enumerate(lines):
    if line.startswith(key + "="):
        lines[index] = f"{key}={value}"
        break
else:
    lines.append(f"{key}={value}")
path.write_text("\n".join(lines) + "\n")
PY
  chmod 600 "$POSTHOG_DIR/.env"
fi

printf 'Iniciando PostHog pelo Compose oficial em http://localhost:8000...\n'
docker compose \
  --progress quiet \
  --project-directory "$POSTHOG_DIR" \
  --env-file "$POSTHOG_DIR/.env" \
  --env-file "$POSTHOG_DIR/.env.services" \
  --project-name taskpilot-posthog \
  -f "$POSTHOG_DIR/docker-compose.hobby.yml" \
  -f "$ROOT_DIR/docker-compose.posthog.yml" \
  up --detach --pull always --no-build

docker compose \
  --progress quiet \
  --project-directory "$POSTHOG_DIR" \
  --env-file "$POSTHOG_DIR/.env" \
  --env-file "$POSTHOG_DIR/.env.services" \
  --project-name taskpilot-posthog \
  -f "$POSTHOG_DIR/docker-compose.hobby.yml" \
  -f "$ROOT_DIR/docker-compose.posthog.yml" \
  up --detach --force-recreate --no-build web

printf 'Aguardando o PostHog concluir a inicialização...\n'
for attempt in {1..120}; do
  status="$(curl --max-time 5 -sS -o /dev/null -w '%{http_code}' http://localhost:8000/_health 2>/dev/null || true)"
  if [ "$status" = "200" ]; then
    printf 'PostHog pronto: http://localhost:8000\n'
    exit 0
  fi
  if (( attempt % 12 == 0 )); then
    printf 'Ainda iniciando (HTTP %s, %s/600 s)...\n' "${status:-sem resposta}" "$((attempt * 5))"
  fi
  sleep 5
done

printf 'Erro: PostHog não respondeu após 10 minutos. Últimos logs do serviço web:\n' >&2
docker compose \
  --progress quiet \
  --project-directory "$POSTHOG_DIR" \
  --env-file "$POSTHOG_DIR/.env" \
  --env-file "$POSTHOG_DIR/.env.services" \
  --project-name taskpilot-posthog \
  -f "$POSTHOG_DIR/docker-compose.hobby.yml" \
  -f "$ROOT_DIR/docker-compose.posthog.yml" \
  logs --tail=80 web >&2
exit 1
