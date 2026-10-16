from __future__ import annotations

import logging
import os
from collections.abc import Mapping
from dataclasses import dataclass
from time import perf_counter
from typing import Any

from opentelemetry import metrics, trace
from opentelemetry._logs import set_logger_provider
from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
from opentelemetry.propagate import extract, inject
from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import Meter, MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.trace import SpanKind, format_span_id, format_trace_id

LOGGER = logging.getLogger(__name__)
_record_factory_configured = False
_worker_metrics: WorkerMetrics | None = None
_metrics_warning_emitted = False


@dataclass(frozen=True)
class WorkerMetrics:
    processed: Any
    duration: Any

    def record(self, outcome: str, elapsed_seconds: float, retry_count: int) -> None:
        if outcome not in {"success", "retry", "failed", "malformed", "error"}:
            raise ValueError(f"Unsupported worker processing outcome: {outcome}")
        if elapsed_seconds < 0 or retry_count < 0:
            raise ValueError("Worker duration and retry count must not be negative.")
        attributes = {
            "outcome": outcome,
            "attempt": "retry" if retry_count else "first",
        }
        self.processed.add(1, attributes)
        self.duration.record(elapsed_seconds, attributes)


def create_worker_metrics(meter: Meter) -> WorkerMetrics:
    return WorkerMetrics(
        processed=meter.create_counter(
            "audio_worker.processed",
            unit="{request}",
            description="Audio extraction delivery attempts processed by the worker.",
        ),
        duration=meter.create_histogram(
            "audio_worker.processing.duration",
            unit="s",
            description="Duration of audio extraction delivery processing.",
        ),
    )


def configure_log_correlation() -> None:
    global _record_factory_configured
    if _record_factory_configured:
        return

    original_factory = logging.getLogRecordFactory()

    def record_factory(*args: Any, **kwargs: Any) -> logging.LogRecord:
        record = original_factory(*args, **kwargs)
        span_context = trace.get_current_span().get_span_context()
        if span_context.is_valid:
            record.trace_id = format_trace_id(span_context.trace_id)
            record.span_id = format_span_id(span_context.span_id)
        else:
            record.trace_id = None
            record.span_id = None
        return record

    logging.setLogRecordFactory(record_factory)
    _record_factory_configured = True


def record_processing(outcome: str, elapsed_seconds: float, retry_count: int) -> None:
    global _metrics_warning_emitted
    if _worker_metrics is None:
        if not _metrics_warning_emitted:
            LOGGER.error("Worker metrics are unavailable because telemetry was not configured.")
            _metrics_warning_emitted = True
        return
    _worker_metrics.record(outcome, elapsed_seconds, retry_count)


def configure_tracing() -> None:
    global _worker_metrics
    endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4318").rstrip("/")
    service_name = os.getenv("OTEL_SERVICE_NAME", "taskpilot-audio-worker")
    resource = Resource.create(
        {
            "service.name": service_name,
            "service.namespace": "taskpilot",
        }
    )

    trace_provider = TracerProvider(resource=resource)
    trace_provider.add_span_processor(
        BatchSpanProcessor(OTLPSpanExporter(endpoint=f"{endpoint}/v1/traces"))
    )
    trace.set_tracer_provider(trace_provider)

    metric_reader = PeriodicExportingMetricReader(
        OTLPMetricExporter(endpoint=f"{endpoint}/v1/metrics")
    )
    meter_provider = MeterProvider(resource=resource, metric_readers=[metric_reader])
    metrics.set_meter_provider(meter_provider)
    _worker_metrics = create_worker_metrics(metrics.get_meter("taskpilot.audio-worker"))

    logger_provider = LoggerProvider(resource=resource)
    logger_provider.add_log_record_processor(
        BatchLogRecordProcessor(OTLPLogExporter(endpoint=f"{endpoint}/v1/logs"))
    )
    set_logger_provider(logger_provider)
    logging.getLogger().addHandler(LoggingHandler(logger_provider=logger_provider))
    configure_log_correlation()


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
    return tracer.start_span("audio_worker.consume_request", context=ctx, kind=SpanKind.CONSUMER)


def inject_trace_headers(headers: dict[str, Any]) -> None:
    inject(headers)


def log_trace_context(message: str) -> None:
    LOGGER.info(message)
