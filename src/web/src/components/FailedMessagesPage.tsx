import { useEffect, useState } from 'react';
import { getFailedMessages, retryFailedMessage } from '../api/client';
import type { FailedMessageSummary } from '../api/types';
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

type RetryState = 'idle' | 'retrying' | 'error';

function FailedMessageRow({
  message,
  onRetried,
}: {
  message: FailedMessageSummary;
  onRetried: (id: number) => void;
}) {
  const [retryState, setRetryState] = useState<RetryState>('idle');

  async function handleRetry() {
    setRetryState('retrying');
    try {
      await retryFailedMessage(message.id);
      onRetried(message.id);
    } catch {
      setRetryState('error');
    }
  }

  return (
    <div className="rounded-md border border-brass/30 bg-paper px-4 py-3 shadow-sm dark:border-brass/25 dark:bg-harbor-2">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0 flex-1 space-y-1">
          <p className="truncate font-mono text-xs text-ink dark:text-bone" title={message.correlationId}>
            {message.correlationId}
          </p>
          <p className="font-sans text-[11px] text-ink/60 dark:text-bone/60">
            {message.originalExchange}
            {message.pointOfInterestId !== null && ` · POI ${message.pointOfInterestId}`}
            {' · '}
            {formatTimestamp(message.failedAt)}
            {' · '}
            {message.retryCount} attempt{message.retryCount === 1 ? '' : 's'}
          </p>
          {message.errorMessage && (
            <p className="font-sans text-xs text-postmark dark:text-postmark-light">
              {message.errorMessage}
            </p>
          )}
          {retryState === 'error' && (
            <p className="font-sans text-xs text-postmark dark:text-postmark-light">
              Retry failed. Try again once the underlying issue is resolved.
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={handleRetry}
          disabled={retryState === 'retrying'}
          className="flex shrink-0 items-center gap-1.5 rounded border border-brass/40 px-2.5 py-1 font-mono text-[10px] tracking-wide text-brass uppercase transition hover:border-postmark hover:text-postmark disabled:cursor-not-allowed disabled:opacity-50 dark:hover:text-postmark-light"
        >
          {retryState === 'retrying' && <Spinner className="h-3 w-3" />}
          Retry
        </button>
      </div>
    </div>
  );
}

export function FailedMessagesPage() {
  const [messages, setMessages] = useState<FailedMessageSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getFailedMessages(controller.signal)
      .then(setMessages)
      .catch((err) => {
        if (err.name !== 'AbortError') setLoadError(true);
      })
      .finally(() => setLoading(false));
    return () => controller.abort();
  }, []);

  function handleRetried(id: number) {
    setMessages((prev) => prev.filter((m) => m.id !== id));
  }

  return (
    <div className="h-full w-full overflow-y-auto pt-20 pb-10 sm:pt-24">
      <div className="mx-auto max-w-3xl px-4 sm:px-6">
        <h2 className="mb-4 font-display text-lg font-medium text-ink dark:text-bone">
          Failed Messages
        </h2>

        {loading && (
          <div className="flex items-center gap-2 text-brass">
            <Spinner className="h-4 w-4" />
            <span className="font-mono text-xs uppercase tracking-wide">Loading</span>
          </div>
        )}

        {!loading && loadError && (
          <p className="font-sans text-sm text-postmark dark:text-postmark-light">
            Could not load failed messages.
          </p>
        )}

        {!loading && !loadError && messages.length === 0 && (
          <p className="font-sans text-sm text-ink/70 dark:text-bone/70">
            No failed messages awaiting retry.
          </p>
        )}

        <div className="space-y-2">
          {messages.map((message) => (
            <FailedMessageRow key={message.id} message={message} onRetried={handleRetried} />
          ))}
        </div>
      </div>
    </div>
  );
}
