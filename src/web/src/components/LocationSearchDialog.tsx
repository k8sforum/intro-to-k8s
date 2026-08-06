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
        className="flex w-full max-w-md flex-col rounded-lg bg-white shadow-lg dark:bg-neutral-900"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="space-y-3 p-5">
          <div className="flex items-start justify-between gap-2">
            <div>
              <h2 className="text-base font-medium text-neutral-900 dark:text-neutral-100">
                Where was this taken?
              </h2>
              <p className="mt-0.5 truncate text-xs text-neutral-500 dark:text-neutral-400">
                {fileName}
              </p>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="text-neutral-400 hover:text-neutral-600 dark:hover:text-neutral-200"
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
              className="w-full rounded-md border border-neutral-300 bg-white px-3 py-2 text-sm text-neutral-900 outline-none focus:border-neutral-500 dark:border-neutral-700 dark:bg-neutral-800 dark:text-neutral-100 dark:focus:border-neutral-500"
            />
            {search.status === "searching" && (
              <Spinner className="absolute top-2.5 right-3 h-4 w-4 text-neutral-400" />
            )}
          </div>

          <div className="max-h-56 overflow-y-auto">
            {search.status === "loaded" && search.places.length === 0 && (
              <p className="px-1 py-2 text-sm text-neutral-500 dark:text-neutral-400">
                No places matched that name.
              </p>
            )}
            {search.status === "error" && (
              <p className="px-1 py-2 text-sm text-red-600 dark:text-red-400">
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
                      className={`w-full rounded-md px-3 py-2 text-left text-sm transition ${
                        selected === place
                          ? "bg-neutral-900 text-white dark:bg-white dark:text-neutral-900"
                          : "text-neutral-700 hover:bg-neutral-100 dark:text-neutral-300 dark:hover:bg-neutral-800"
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

        <div className="flex justify-end gap-2 border-t border-neutral-200 px-5 py-3 dark:border-neutral-800">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100 dark:text-neutral-300 dark:hover:bg-neutral-800"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!selected}
            onClick={() => selected && onSelect(selected)}
            className="rounded-full bg-neutral-900 px-4 py-2 text-sm font-medium text-white shadow-md transition hover:bg-neutral-700 disabled:cursor-not-allowed disabled:opacity-60 dark:bg-white dark:text-neutral-900 dark:hover:bg-neutral-200"
          >
            Upload here
          </button>
        </div>
      </div>
    </div>
  );
}
