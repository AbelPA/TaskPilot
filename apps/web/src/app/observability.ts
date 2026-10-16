import { context, propagation, Span, SpanStatusCode, trace } from '@opentelemetry/api';
import { W3CTraceContextPropagator } from '@opentelemetry/core';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-base';
import { WebTracerProvider } from '@opentelemetry/sdk-trace-web';

let initialized = false;

export function initializeTelemetry(): void {
  if (initialized || typeof window === 'undefined') {
    return;
  }

  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      'service.name': 'taskpilot-web',
      'service.namespace': 'taskpilot',
    }),
    spanProcessors: [
      new BatchSpanProcessor(
        new OTLPTraceExporter({
          url: `http://${window.location.hostname}:4319/v1/traces`,
        }),
      ),
    ],
  });

  provider.register({ propagator: new W3CTraceContextPropagator() });
  initialized = true;
}

export function startHttpSpan(name: string): { span: Span; headers: Record<string, string> } {
  initializeTelemetry();
  const span = trace.getTracer('taskpilot.web').startSpan(name);
  const carrier: Record<string, string> = {};
  propagation.inject(trace.setSpan(context.active(), span), carrier);
  return { span, headers: carrier };
}

export function finishHttpSpan(span: Span, failed: boolean): void {
  span.setStatus({ code: failed ? SpanStatusCode.ERROR : SpanStatusCode.OK });
  span.end();
}
