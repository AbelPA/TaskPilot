import logging
import os
from collections.abc import Mapping
from typing import Any

from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.propagate import extract, inject
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.trace import format_span_id, format_trace_id

LOGGER = logging.getLogger(__name__)


def configure_tracing() -> None:
    endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-lgtm:4318")
    exporter = OTLPSpanExporter(endpoint=f"{endpoint.rstrip('/')}/v1/traces")
    resource = Resource.create(
        {
            "service.name": os.getenv("OTEL_SERVICE_NAME", "taskpilot-audio-worker"),
            "service.namespace": "taskpilot",
        }
    )
    provider = TracerProvider(resource=resource)
    provider.add_span_processor(BatchSpanProcessor(exporter))
    trace.set_tracer_provider(provider)


def start_consumer_span(headers: Mapping[str, Any] | None) -> Any:
    carrier: dict[str, str] = {}
    if headers:
        for name in ("traceparent", "tracestate"):
            value = headers.get(name)
            if isinstance(value, bytes):
                value = value.decode("utf-8")
            if isinstance(value, str) and value:
                carrier[name] = value
    ctx = extract(carrier)
    tracer = trace.get_tracer("taskpilot.audio-worker")
    return tracer.start_span("audio_worker.consume_request", context=ctx)


def inject_trace_headers(headers: dict[str, Any]) -> None:
    inject(headers)


def log_trace_context(message: str, span: Any | None = None) -> None:
    span_context = span.get_span_context() if span is not None else trace.get_current_span().get_span_context()
    if not span_context.is_valid:
        LOGGER.info(message)
        return
    LOGGER.info(
        "%s trace_id=%s span_id=%s",
        message,
        format_trace_id(span_context.trace_id),
        format_span_id(span_context.span_id),
    )
