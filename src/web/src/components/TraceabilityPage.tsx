import { useEffect, useState } from 'react';
import { getCorrelationEvents, getCorrelationSummaries } from '../api/client';
import type { CorrelationSummary, MessageAuditEvent } from '../api/types';
import { Spinner } from './Spinner';

function formatTimestamp(value: string) {
  return new Date(value).toLocaleString('en-GB', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

function StatusBadge({ hasFailure }: { hasFailure: boolean }) {
  return (
    <span
      className={
        'rounded-full border px-2.5 py-0.5 font-mono text-[10px] tracking-wide uppercase ' +
        (hasFailure
          ? 'border-postmark/50 bg-postmark/10 text-postmark dark:border-postmark-light/50 dark:text-postmark-light'
          : 'border-brass/40 bg-brass/10 text-ink dark:border-brass/30 dark:text-bone')
      }
    >
      {hasFailure ? 'Failure' : 'Healthy'}
    </span>
  );
}

function EventTypeBadge({ eventType }: { eventType: string }) {
  const isFailed = eventType === 'Failed';
  const isRetried = eventType === 'Retried';
  return (
    <span
      className={
        'rounded-full border px-2 py-0.5 font-mono text-[10px] tracking-wide uppercase ' +
        (isFailed
          ? 'border-postmark/50 bg-postmark/10 text-postmark dark:border-postmark-light/50 dark:text-postmark-light'
          : isRetried
            ? 'border-brass/50 bg-brass/10 text-brass'
            : 'border-brass/30 bg-transparent text-ink dark:text-bone')
      }
    >
      {eventType}
    </span>
  );
}

interface EventsState {
  status: 'loading' | 'loaded' | 'error';
  events: MessageAuditEvent[];
}

export function TraceabilityPage() {
  const [summaries, setSummaries] = useState<CorrelationSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [eventsByCorrelation, setEventsByCorrelation] = useState<Record<string, EventsState>>({});

  useEffect(() => {
    const controller = new AbortController();
    getCorrelationSummaries(1, 50, controller.signal)
      .then(setSummaries)
      .catch(() => {})
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, []);

  function toggleRow(correlationId: string) {
    if (expandedId === correlationId) {
      setExpandedId(null);
      return;
    }
    setExpandedId(correlationId);
    if (!eventsByCorrelation[correlationId]) {
      setEventsByCorrelation((prev) => ({ ...prev, [correlationId]: { status: 'loading', events: [] } }));
      getCorrelationEvents(correlationId)
        .then((events) =>
          setEventsByCorrelation((prev) => ({ ...prev, [correlationId]: { status: 'loaded', events } })),
        )
        .catch(() =>
          setEventsByCorrelation((prev) => ({ ...prev, [correlationId]: { status: 'error', events: [] } })),
        );
    }
  }

  return (
    <div className="h-full w-full overflow-y-auto pt-20 pb-10 sm:pt-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <h2 className="mb-4 font-display text-lg font-medium text-ink dark:text-bone">
          Message Traceability
        </h2>

        {loading && (
          <div className="flex items-center gap-2 text-brass">
            <Spinner className="h-4 w-4" />
            <span className="font-mono text-xs uppercase tracking-wide">Loading</span>
          </div>
        )}

        {!loading && summaries.length === 0 && (
          <p className="font-sans text-sm text-ink/70 dark:text-bone/70">
            No message activity recorded yet.
          </p>
        )}

        <div className="space-y-2">
          {summaries.map((summary) => {
            const expanded = expandedId === summary.correlationId;
            const eventsState = eventsByCorrelation[summary.correlationId];

            return (
              <div
                key={summary.correlationId}
                className="rounded-md border border-brass/30 bg-paper shadow-sm dark:border-brass/25 dark:bg-harbor-2"
              >
                <button
                  type="button"
                  onClick={() => toggleRow(summary.correlationId)}
                  className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left"
                >
                  <div className="min-w-0 flex-1">
                    <p
                      className="truncate font-mono text-xs text-ink dark:text-bone"
                      title={summary.correlationId}
                    >
                      {summary.correlationId}
                    </p>
                    <p className="mt-0.5 font-sans text-[11px] text-ink/60 dark:text-bone/60">
                      {formatTimestamp(summary.startedAt)} &rarr; {formatTimestamp(summary.lastEventAt)}
                      {' · '}
                      {summary.eventCount} event{summary.eventCount === 1 ? '' : 's'}
                    </p>
                  </div>
                  <StatusBadge hasFailure={summary.hasFailure} />
                  <span className="text-brass">{expanded ? '−' : '+'}</span>
                </button>

                {expanded && (
                  <div className="border-t border-brass/20 px-4 py-3">
                    {(!eventsState || eventsState.status === 'loading') && (
                      <div className="flex items-center gap-2 text-brass">
                        <Spinner className="h-3.5 w-3.5" />
                        <span className="font-mono text-[10px] uppercase tracking-wide">Loading events</span>
                      </div>
                    )}
                    {eventsState?.status === 'error' && (
                      <p className="font-sans text-xs text-postmark dark:text-postmark-light">
                        Could not load events.
                      </p>
                    )}
                    {eventsState?.status === 'loaded' && (
                      <ol className="space-y-2">
                        {eventsState.events.map((event, index) => (
                          <li
                            key={`${event.exchangeName}-${event.createdAt}-${index}`}
                            className="flex flex-wrap items-center gap-2 font-sans text-xs text-ink dark:text-bone"
                          >
                            <span className="font-mono text-[10px] text-ink/50 dark:text-bone/50">
                              {formatTimestamp(event.createdAt)}
                            </span>
                            <EventTypeBadge eventType={event.eventType} />
                            <span className="text-ink/80 dark:text-bone/80">{event.exchangeName}</span>
                            {event.retryCount > 0 && (
                              <span className="font-mono text-[10px] text-brass">
                                retry {event.retryCount}
                              </span>
                            )}
                            {event.errorMessage && (
                              <span className="text-postmark dark:text-postmark-light">
                                {event.errorMessage}
                              </span>
                            )}
                          </li>
                        ))}
                      </ol>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
