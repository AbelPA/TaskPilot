#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"

if [ -z "${YOUTUBE_DATA_API_KEY:-}" ] ||
  [ "$YOUTUBE_DATA_API_KEY" = "replace-with-a-youtube-data-api-key" ]; then
  printf 'Erro: defina YOUTUBE_DATA_API_KEY no ambiente do shell antes de iniciar a aplicação.\n' >&2
  printf "Exemplo: export YOUTUBE_DATA_API_KEY='sua-chave'; ./scripts/start-local.sh\n" >&2
  exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
  printf 'Erro: Docker não está instalado ou não está disponível no PATH.\n' >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  printf 'Erro: Docker Compose v2 não está disponível. Instale ou atualize o Docker.\n' >&2
  exit 1
fi

if ! docker info >/dev/null 2>&1; then
  printf 'Docker não está em execução; tentando iniciá-lo...\n'
  case "$(uname -s)" in
    Darwin)
      if ! open -a Docker; then
        printf 'Erro: não foi possível iniciar o Docker Desktop. Verifique se está instalado.\n' >&2
        exit 1
      fi
      ;;
    Linux)
      if ! command -v systemctl >/dev/null 2>&1; then
        printf 'Erro: Docker não está em execução e systemctl não está disponível para iniciá-lo.\n' >&2
        exit 1
      fi
      sudo systemctl start docker
      ;;
    *)
      printf 'Erro: inicie o Docker neste sistema operacional e execute o script novamente.\n' >&2
      exit 1
      ;;
  esac

  docker_ready=0
  for _ in {1..60}; do
    if docker info >/dev/null 2>&1; then
      docker_ready=1
      break
    fi
    sleep 2
  done

  if [ "$docker_ready" -ne 1 ]; then
    printf 'Erro: o Docker não ficou pronto após 120 segundos.\n' >&2
    exit 1
  fi
fi

if [ ! -f "$ROOT_DIR/.env" ]; then
  cp "$ROOT_DIR/.env.example" "$ROOT_DIR/.env"
  printf 'Criado .env a partir de .env.example.\n'
fi

cd "$ROOT_DIR"
printf '\nIniciando a aplicação e suas dependências...\n'
docker compose up --detach --build --wait

printf '\nAplicação iniciada:\n'
printf '  Web:             http://localhost:4200\n'
printf '  RabbitMQ:        http://localhost:15672\n'
printf '  API S3:          http://localhost:8333\n'
printf '  Console filer:   http://localhost:8888\n'
printf '\nMonitorando logs em busca de erros (Ctrl+C encerra o monitoramento; os serviços continuam ativos)...\n'
docker compose logs --follow --tail=100 2>&1 |
  awk 'tolower($0) ~ /error|fatal|exception|traceback|failed|unhealthy/ { print; fflush() }'
