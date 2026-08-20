import { WebTracerProvider, BatchSpanProcessor } from '@opentelemetry/sdk-trace-web'
import { ZoneContextManager } from '@opentelemetry/context-zone'
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http'
import { resourceFromAttributes } from '@opentelemetry/resources'
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions'
import { registerInstrumentations } from '@opentelemetry/instrumentation'
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load'
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch'

const otlpEndpoint = import.meta.env.VITE_OTEL_EXPORTER_OTLP_TRACES_ENDPOINT
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5101'
// propagateTraceHeaderCorsUrls does an exact string match against the full
// request URL, not just the origin — a RegExp is needed to cover every path.
const escapedApiBaseUrl = apiBaseUrl.replace(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`)
const apiUrlPattern = new RegExp('^' + escapedApiBaseUrl)

if (otlpEndpoint) {
  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({ [ATTR_SERVICE_NAME]: 'mytravels-web' }),
    spanProcessors: [new BatchSpanProcessor(new OTLPTraceExporter({ url: otlpEndpoint }))],
  })

  provider.register({ contextManager: new ZoneContextManager() })

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({ propagateTraceHeaderCorsUrls: [apiUrlPattern] }),
    ],
  })
}
