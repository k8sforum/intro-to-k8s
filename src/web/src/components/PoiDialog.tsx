import { useEffect, useState } from "react";
import type { PointOfInterest } from "../api/types";
import { getPointOfInterestImage } from "../api/client";
import { Spinner } from "./Spinner";

interface PoiDialogProps {
  poi: PointOfInterest;
  onClose: () => void;
}

function formatDateCreated(value: string) {
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
      className="h-12 w-12 text-neutral-300 dark:text-neutral-600"
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
        className="w-full max-w-md rounded-lg bg-white shadow-lg dark:bg-neutral-900"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="relative flex aspect-video w-full items-center justify-center overflow-hidden rounded-t-lg bg-neutral-100 dark:bg-neutral-800">
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
            <div className="absolute inset-0 flex items-center justify-center bg-neutral-100/60 dark:bg-neutral-800/60">
              <Spinner className="h-6 w-6 text-neutral-400" />
            </div>
          )}
        </div>

        <div className="space-y-3 p-5">
          <div className="flex items-start justify-between gap-2">
            <h2 className="text-base font-medium text-neutral-900 dark:text-neutral-100">
              {poi.formattedAddress || "Address pending"}
            </h2>
            <button
              type="button"
              onClick={onClose}
              className="text-neutral-400 hover:text-neutral-600 dark:hover:text-neutral-200"
              aria-label="Close"
            >
              ✕
            </button>
          </div>

          <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm text-neutral-500 dark:text-neutral-400">
            <dt>Added</dt>
            <dd className="text-neutral-800 dark:text-neutral-200">
              {formatDateCreated(poi.dateCreated)}
            </dd>
          </dl>
        </div>
      </div>
    </div>
  );
}
