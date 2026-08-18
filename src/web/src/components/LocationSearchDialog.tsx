import { useEffect, useState } from "react";
import { searchPlaces } from "../api/client";
import type { Place } from "../api/types";
import { Spinner } from "./Spinner";

const DEBOUNCE_MS = 400;
const MIN_QUERY_LENGTH = 3;

interface LocationSearchDialogProps {
  fileName: string;
  onSelect: (place: Place) => void;
  onClose: () => void;
}

type SearchState =
  | { status: "idle" }
  | { status: "searching" }
  | { status: "loaded"; places: Place[] }
  | { status: "error" };

export function LocationSearchDialog({
  fileName,
  onSelect,
  onClose,
}: LocationSearchDialogProps) {
  const [query, setQuery] = useState("");
  const [search, setSearch] = useState<SearchState>({ status: "idle" });
  const [selected, setSelected] = useState<Place | null>(null);

  useEffect(() => {
    const trimmed = query.trim();
    if (trimmed.length < MIN_QUERY_LENGTH) {
      setSearch({ status: "idle" });
      return;
    }

    const controller = new AbortController();
    const timer = setTimeout(() => {
      setSearch({ status: "searching" });
      searchPlaces(trimmed, controller.signal)
        .then((places) => setSearch({ status: "loaded", places }))
        .catch((err) => {
          if (err.name !== "AbortError") setSearch({ status: "error" });
        });
    }, DEBOUNCE_MS);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [query]);

  return (
    <div
      className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="flex w-full max-w-md flex-col rounded-md border border-brass/30 bg-paper shadow-xl dark:border-brass/25 dark:bg-harbor-2"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="space-y-3 p-5">
          <div className="flex items-start justify-between gap-2">
            <div>
              <h2 className="font-display text-lg font-medium text-ink italic dark:text-bone">
                Where was this taken?
              </h2>
              <p className="mt-0.5 truncate font-mono text-xs text-brass">
                {fileName}
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="text-brass transition hover:text-postmark dark:hover:text-postmark-light"
              aria-label="Close"
            >
              ✕
            </button>
          </div>

          <div className="relative">
            <input
              type="search"
              autoFocus
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setSelected(null);
              }}
              placeholder="Search for a place, e.g. Table Mountain"
              className="w-full rounded-md border border-brass/40 bg-bone px-3 py-2 font-sans text-sm text-ink outline-none focus:border-postmark dark:border-brass/30 dark:bg-harbor dark:text-bone dark:focus:border-postmark-light"
            />
            {search.status === "searching" && (
              <Spinner className="absolute top-2.5 right-3 h-4 w-4 text-brass" />
            )}
          </div>

          <div className="max-h-56 overflow-y-auto">
            {search.status === "loaded" && search.places.length === 0 && (
              <p className="px-1 py-2 font-sans text-sm text-ink/60 dark:text-bone/60">
                No places matched that name.
              </p>
            )}
            {search.status === "error" && (
              <p className="px-1 py-2 font-sans text-sm text-postmark dark:text-postmark-light">
                Search failed. Please try again.
              </p>
            )}
            {search.status === "loaded" && (
              <ul className="space-y-1">
                {search.places.map((place) => (
                  <li key={`${place.latitude},${place.longitude}`}>
                    <button
                      type="button"
                      onClick={() => setSelected(place)}
                      className={`w-full rounded-md px-3 py-2 text-left font-sans text-sm transition ${
                        selected === place
                          ? "bg-postmark text-bone dark:bg-postmark-light dark:text-ink"
                          : "text-ink hover:bg-brass/10 dark:text-bone dark:hover:bg-brass/10"
                      }`}
                    >
                      {place.formattedAddress}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>

        <div className="flex justify-end gap-2 border-t border-brass/20 px-5 py-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md px-4 py-2 font-sans text-sm font-medium text-ink/70 transition hover:bg-brass/10 dark:text-bone/70 dark:hover:bg-brass/10"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!selected}
            onClick={() => selected && onSelect(selected)}
            className="rounded-md bg-ink px-4 py-2 font-sans text-sm font-medium text-bone shadow-md transition hover:bg-ink-2 disabled:cursor-not-allowed disabled:opacity-60 dark:bg-harbor dark:hover:bg-harbor-2"
          >
            Pin this place
          </button>
        </div>
      </div>
    </div>
  );
}
