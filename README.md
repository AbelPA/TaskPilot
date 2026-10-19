# TaskPilot

## What is it?

TaskPilot is an open-source developer productivity platform designed to automate repetitive tasks and orchestrate development workflows.

It brings together command-line automation, AI model routing, prompt management, agents, and workflows into a single, extensible platform.

The goal is to reduce the operational work involved in software development, allowing developers to focus more on engineering decisions, problem solving, architecture, and delivering value.

The project is designed as a modular, polyglot monorepo, allowing different applications, services, and tools to evolve independently while sharing a common foundation.

## Running all tests

From the repository root, run:

```bash
./scripts/test.sh
```

The script runs the frontend tests, API tests, audio worker tests, and OpenAPI
contract validation. Output is shown as each suite runs, followed by a summary.
It runs every suite even if one fails and exits with a non-zero status if any
suite fails.

The required tools are Node.js/npm, .NET SDK, and uv. Install the web
dependencies first with `npm ci` from `apps/web`.

## Running the application locally

From the repository root, run:

```bash
./scripts/start-local.sh
```

The script starts Docker if needed, creates `.env` from `.env.example` if it
does not exist, and builds and starts the complete Docker Compose stack.
Open the **Configurações** screen (<http://localhost:4200/settings>) and save the
YouTube Data API key there; it is stored encrypted (AES-256-GCM) in the database
using `SETTINGS_ENCRYPTION_KEY`, which the script generates in `.env`. Keep that
value: without it, saved keys cannot be decrypted. The Settings endpoints have no authentication, so expose them
only on a trusted local environment. The web app is
available at <http://localhost:4200>; RabbitMQ management is at
<http://localhost:15672>; the local Grafana/OTel LGTM dashboard is at
<http://localhost:3000>. SeaweedFS provides the local S3 API at
<http://localhost:8333> and the filer console at <http://localhost:8888>.
The startup script stays open and prints log lines matching common error indicators;
press `Ctrl+C` to stop monitoring without stopping the containers. To view all
service output, use `docker compose logs --follow`; stop the services with
`docker compose down`. The browser exports spans to the local OTLP collector at
`http://localhost:4319`, which forwards traces, logs, and metrics to the LGTM
stack. The API and worker also export only through this collector. In Grafana
Explore, use Tempo to find a trace by service and time window, then search Loki
for log records using its `trace_id`; `requestId` remains a separate business
identifier. Prometheus metrics include HTTP request duration/counts and the
worker's `audio_worker.processed` and `audio_worker.processing.duration`
instruments. See [the distributed observability quickstart](specs/003-distributed-observability-poc/quickstart.md)
for the end-to-end steps and troubleshooting.

### Session replay with PostHog

The web container supports PostHog session replay. The local Compose stack runs
PostHog alongside TaskPilot at <http://localhost:8000>. Its first start downloads
PostHog's current hobby Compose files and required configuration into the ignored
`.local/posthog/` directory; those files remain separate from TaskPilot's Compose
project so service names, volumes, and ports do not collide. PostHog's hobby stack
requires at least 8 GB of memory. See the [official self-hosting guide](https://posthog.com/docs/self-host).

Start the whole local stack as usual:

```bash
./scripts/start-local.sh
```

Open <http://localhost:8000>, create a project, and enable **Record user
sessions** in its settings. Set `POSTHOG_PROJECT_TOKEN` in TaskPilot's `.env` to
that project's token, then rebuild the web container with
`docker compose up -d --build web`. `POSTHOG_HOST` defaults to
`http://localhost:8000`; browser capture stays disabled while the project token
is empty. The browser SDK masks form inputs by default; review PostHog's
[privacy guidance](https://posthog.com/docs/privacy) before collecting real user sessions.
Stop only PostHog with `./scripts/stop-posthog-local.sh`; its data volumes are
preserved.
