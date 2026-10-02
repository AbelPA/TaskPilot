FROM python:3.12-slim AS runtime
ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy
RUN apt-get update \
    && apt-get install --no-install-recommends -y ffmpeg ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=ghcr.io/astral-sh/uv:0.11.17 /uv /uvx /usr/local/bin/
WORKDIR /app
COPY apps/workers/audio/pyproject.toml apps/workers/audio/uv.lock ./
RUN uv sync --locked --no-dev --no-install-project
COPY apps/workers/audio/src ./src
RUN uv sync --locked --no-dev
ENV PATH="/app/.venv/bin:$PATH" \
    PYTHONPATH="/app/src"
RUN useradd --create-home --uid 10001 worker \
    && chown -R worker:worker /app
USER worker
CMD ["python", "-m", "audio_worker.main"]
