import logging

from opentelemetry import trace
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import InMemoryMetricReader
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import SimpleSpanProcessor
from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter
from opentelemetry.trace import SpanContext, SpanKind, TraceFlags, TraceState

from audio_worker.observability import (
    configure_log_correlation,
    create_worker_metrics,
    start_consumer_span,
)


def _provider(monkeypatch):
    exporter = InMemorySpanExporter()
    provider = TracerProvider()
    provider.add_span_processor(SimpleSpanProcessor(exporter))
    monkeypatch.setattr(trace, "get_tracer", provider.get_tracer)
    return provider, exporter


def test_consumer_span_continues_valid_w3c_context(monkeypatch):
    provider, exporter = _provider(monkeypatch)
    remote = SpanContext(
        trace_id=0x1234567890ABCDEF1234567890ABCDEF,
        span_id=0x1234567890ABCDEF,
        is_remote=True,
        trace_flags=TraceFlags(TraceFlags.SAMPLED),
        trace_state=TraceState(),
    )
    parent = trace.get_tracer("test").start_span("producer", context=trace.set_span_in_context(trace.NonRecordingSpan(remote)))
    traceparent = f"00-{remote.trace_id:032x}-{remote.span_id:016x}-01"

    span = start_consumer_span({"traceparent": traceparent, "tracestate": "vendor=value"})
    with span:
        assert span.get_span_context().trace_id == remote.trace_id

    parent.end()
    spans = exporter.get_finished_spans()
    consumer = next(item for item in spans if item.name == "audio_worker.consume_request")
    assert consumer.context.trace_id == remote.trace_id
    assert consumer.parent.span_id == remote.span_id
    assert consumer.parent.is_remote
    assert consumer.kind is SpanKind.CONSUMER
    assert consumer.context.trace_state.get("vendor") == "value"
    provider.shutdown()


def test_consumer_span_starts_new_trace_when_context_is_missing_or_invalid(monkeypatch):
    provider, _ = _provider(monkeypatch)
    for headers in (None, {}, {"traceparent": "not-a-traceparent"}):
        span = start_consumer_span(headers)
        assert span.get_span_context().is_valid
        assert span.get_span_context().trace_id != 0x1234567890ABCDEF1234567890ABCDEF
        span.end()
    provider.shutdown()


def test_worker_logs_include_active_trace_context_and_separate_request_id(monkeypatch, caplog):
    provider, _ = _provider(monkeypatch)
    configure_log_correlation()
    span = trace.get_tracer("test").start_span("worker")

    with caplog.at_level(logging.INFO), trace.use_span(span, end_on_exit=False):
        logging.getLogger("test.worker").info("processed", extra={"request_id": "business-id"})

    record = next(item for item in caplog.records if item.message == "processed")
    assert record.trace_id == f"{span.get_span_context().trace_id:032x}"
    assert record.span_id == f"{span.get_span_context().span_id:016x}"
    assert record.request_id == "business-id"
    provider.shutdown()


def test_worker_metrics_record_outcome_duration_and_bounded_attempt_attributes():
    reader = InMemoryMetricReader()
    provider = MeterProvider(metric_readers=[reader])
    instruments = create_worker_metrics(provider.get_meter("test.worker"))
    instruments.record("success", 1.25, 0)
    instruments.record("retry", 0.4, 2)

    metrics_data = reader.get_metrics_data()
    metrics_by_name = {
        metric.name: metric
        for resource in metrics_data.resource_metrics
        for scope in resource.scope_metrics
        for metric in scope.metrics
    }
    assert metrics_by_name["audio_worker.processed"].data.data_points[0].value == 1
    assert metrics_by_name["audio_worker.processing.duration"].data.data_points
    attributes = {
        tuple(sorted(point.attributes.items()))
        for point in metrics_by_name["audio_worker.processed"].data.data_points
    }
    assert (('attempt', 'first'), ('outcome', 'success')) in attributes
    assert (('attempt', 'retry'), ('outcome', 'retry')) in attributes
    assert all(
        key not in {"trace_id", "span_id", "request_id", "url"}
        for point in metrics_by_name["audio_worker.processed"].data.data_points
        for key in point.attributes
    )
    provider.shutdown()
