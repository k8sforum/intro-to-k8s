import { useEffect, useState } from "react";
import type { PointOfInterest } from "../api/types";
import { getPointOfInterestImage } from "../api/client";
import { Spinner } from "./Spinner";
import { PostmarkGlyph } from "./icons";

interface PoiDialogProps {
  poi: PointOfInterest;
  onClose: () => void;
}

function formatDate(value: string) {
  const date = new Date(value);
  const weekday = date.toLocaleDateString("en-GB", { weekday: "long" });
  const day = date.getDate();
  const month = date.toLocaleDateString("en-GB", { month: "long" });
  const year = date.getFullYear();
  const hours = date.getHours();
  const minutes = date.getMinutes().toString().padStart(2, "0");
  return `${weekday}, ${day} ${month} ${year} at ${hours}h${minutes}m`;
}

type ImageState =
  | { status: "loading" }
  | { status: "loaded"; src: string }
  | { status: "error" };

function ImagePlaceholderIcon() {
  return (
    <svg
      className="h-12 w-12 text-brass/40 dark:text-brass/30"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <circle cx="8.5" cy="8.5" r="1.5" />
      <path d="M21 15l-5-5L5 21" />
    </svg>
  );
}

export function PoiDialog({ poi, onClose }: PoiDialogProps) {
  const [image, setImage] = useState<ImageState>({ status: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    setImage({ status: "loading" });

    getPointOfInterestImage(poi.id, true, controller.signal)
      .then((base64) =>
        setImage({ status: "loaded", src: `data:image/jpeg;base64,${base64}` }),
      )
      .catch((err) => {
        if (err.name !== "AbortError") setImage({ status: "error" });
      });

    return () => controller.abort();
  }, [poi.id]);

  return (
    <div
      className="fixed inset-0 z-[1000] flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-md border border-brass/30 bg-paper shadow-xl dark:border-brass/25 dark:bg-harbor-2"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="p-2">
          <div className="relative flex aspect-video w-full items-center justify-center overflow-hidden rounded-sm bg-ink/5 ring-1 ring-brass/25 dark:bg-bone/5">
            {image.status === "loaded" ? (
              <img
                src={image.src}
                alt={poi.formattedAddress}
                className="h-full w-full object-cover"
              />
            ) : (
              <ImagePlaceholderIcon />
            )}
            {image.status === "loading" && (
              <div className="absolute inset-0 flex items-center justify-center bg-paper/60 dark:bg-harbor-2/60">
                <Spinner className="h-6 w-6 text-brass" />
              </div>
            )}
          </div>
        </div>

        <div className="space-y-4 px-5 pt-1 pb-5">
          <div className="flex items-start justify-between gap-2">
            <h2 className="font-display text-lg font-medium text-ink dark:text-bone">
              {poi.formattedAddress || "Address pending"}
            </h2>
            <button
              type="button"
              onClick={onClose}
              className="text-brass transition hover:text-postmark dark:hover:text-postmark-light"
              aria-label="Close"
            >
              ✕
            </button>
          </div>

          <div className="flex items-center gap-3 border-t border-brass/20 pt-4">
            <div className="flex h-10 w-10 shrink-0 -rotate-6 items-center justify-center rounded-full border border-dashed border-postmark/60 text-postmark dark:border-postmark-light/60 dark:text-postmark-light">
              <PostmarkGlyph className="h-5 w-5" />
            </div>
            <div>
              <p className="font-mono text-[10px] tracking-[0.16em] text-brass uppercase">
                Taken
              </p>
              <p className="font-sans text-sm text-ink dark:text-bone">
                {formatDate(poi.dateTaken ?? poi.dateCreated)}
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
