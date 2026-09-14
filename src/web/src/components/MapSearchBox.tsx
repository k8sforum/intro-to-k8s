import { useEffect, useRef, useState } from 'react';
import { searchPointsOfInterest } from '../api/client';
import type { PointOfInterest } from '../api/types';
import { Spinner } from './Spinner';
import { SearchGlyph } from './icons';

const DEBOUNCE_MS = 500;
const MIN_TERM_LENGTH = 4;

type SearchState =
  | { status: 'idle' }
  | { status: 'searching' }
  | { status: 'loaded'; count: number; term: string }
  | { status: 'error' };

interface MapSearchBoxProps {
  /** Hands up the matched points, or null to put every pin back on the map. */
  onResults: (pois: PointOfInterest[] | null) => void;
}

/**
 * A floating search control over the map. Collapsed it is a single button; open it animates a text
 * box out beside it. Results replace the pins on the map until the box is emptied or hidden.
 */
export function MapSearchBox({ onResults }: MapSearchBoxProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false);
  const [term, setTerm] = useState('');
  const [search, setSearch] = useState<SearchState>({ status: 'idle' });

  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  useEffect(() => {
    const trimmed = term.trim();

    // Emptying the box is the explicit "show me everything again" gesture. Falling below the
    // minimum length is not — the last result set stays on the map until it is emptied or hidden,
    // so backspacing a couple of characters doesn't flash the whole library back up.
    if (trimmed.length === 0) {
      setSearch({ status: 'idle' });
      onResults(null);
      return;
    }

    if (trimmed.length < MIN_TERM_LENGTH) return;

    const controller = new AbortController();
    const timer = setTimeout(() => {
      setSearch({ status: 'searching' });
      searchPointsOfInterest(trimmed, controller.signal)
        .then((pois) => {
          setSearch({ status: 'loaded', count: pois.length, term: trimmed });
          onResults(pois);
        })
        .catch((err: Error) => {
          if (err.name !== 'AbortError') setSearch({ status: 'error' });
        });
    }, DEBOUNCE_MS);

    // Runs on every keystroke, so the pending timer is dropped and any in-flight request aborted.
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [term, onResults]);

  function hide() {
    setOpen(false);
    setTerm('');
    setSearch({ status: 'idle' });
    onResults(null);
  }

  const message = describe(search);

  return (
    // The control sits over the map rather than inside it, but stop pointer and wheel events here
    // anyway so typing or scrolling on the box can never pan or zoom the map underneath.
    <div
      className="flex flex-col items-start gap-2"
      onClick={(e) => e.stopPropagation()}
      onDoubleClick={(e) => e.stopPropagation()}
      onMouseDown={(e) => e.stopPropagation()}
      onWheel={(e) => e.stopPropagation()}
      onKeyDown={(e) => {
        if (e.key === 'Escape') hide();
      }}
    >
      {open && message && (
        <p className="max-w-72 rounded-md border border-brass/40 bg-paper/95 px-3 py-2 font-sans text-xs text-ink/80 shadow-md backdrop-blur-sm dark:border-brass/30 dark:bg-harbor-2/95 dark:text-bone/80">
          {message}
        </p>
      )}

      <div className="flex items-stretch overflow-hidden rounded-md border border-brass/40 bg-ink shadow-md dark:border-brass/30 dark:bg-harbor-2">
        <button
          type="button"
          onClick={() => (open ? hide() : setOpen(true))}
          aria-label={open ? 'Hide search' : 'Search points of interest'}
          aria-expanded={open}
          className="px-3 py-2.5 text-bone transition hover:bg-ink-2 dark:hover:bg-harbor"
        >
          {open ? (
            <span aria-hidden="true" className="block h-4 w-4 text-center text-sm leading-4">
              ✕
            </span>
          ) : (
            <SearchGlyph className="h-4 w-4 text-postmark-light" />
          )}
        </button>

        <div
          className={`flex items-center overflow-hidden transition-[width,opacity] duration-200 ease-out motion-reduce:transition-none ${
            open ? 'w-56 opacity-100 sm:w-72' : 'w-0 opacity-0'
          }`}
        >
          <span className="my-2 w-px shrink-0 bg-brass/30" aria-hidden="true" />
          <input
            ref={inputRef}
            type="search"
            value={term}
            onChange={(e) => setTerm(e.target.value)}
            aria-label="Search points of interest"
            placeholder="Search address, tags, description"
            tabIndex={open ? 0 : -1}
            className="w-full bg-transparent px-3 py-2.5 font-sans text-sm text-bone placeholder:text-bone/40 outline-none"
          />
          {search.status === 'searching' && (
            <Spinner className="mr-3 h-4 w-4 shrink-0 text-brass" />
          )}
        </div>
      </div>

      <p className="sr-only" role="status" aria-live="polite">
        {message}
      </p>
    </div>
  );
}

/** The three states the control has to read as distinctly: searching, a count, and no matches. */
function describe(search: SearchState): string {
  switch (search.status) {
    case 'searching':
      return 'Searching…';
    case 'loaded':
      return search.count === 0
        ? `No points match “${search.term}”. The map is empty, not broken — clear the box to bring every pin back.`
        : `${search.count} ${search.count === 1 ? 'point' : 'points'} on the map.`;
    case 'error':
      return 'Search failed. Please try again.';
    default:
      return '';
  }
}
