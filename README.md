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
Export `YOUTUBE_DATA_API_KEY` in your shell before running the script; it is
read from the process environment and is not stored in `.env`. The web app is
available at <http://localhost:4200>; RabbitMQ management is at
<http://localhost:15672>. SeaweedFS provides the local S3 API at
<http://localhost:8333> and the filer console at <http://localhost:8888>.
Use `docker compose logs --follow` to view service output and
`docker compose down` to stop the services.

On macOS, enter the key without echoing it or saving it in shell history:

```bash
printf 'YouTube Data API key: '
read -r -s YOUTUBE_DATA_API_KEY
printf '\n'
export YOUTUBE_DATA_API_KEY
./scripts/start-local.sh
```
