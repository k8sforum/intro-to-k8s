import { useEffect, useState } from "react";
import type { PointOfInterest } from "../api/types";
import { getPointOfInterestImage } from "../api/client";

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
        <div className="flex aspect-video w-full items-center justify-center overflow-hidden rounded-t-lg bg-neutral-100 dark:bg-neutral-800">
          {image.status === "loaded" && (
            <img
              src={image.src}
              alt={poi.formattedAddress}
              className="h-full w-full object-cover"
            />
          )}
          {image.status === "loading" && (
            <span className="text-sm text-neutral-400">Loading image…</span>
          )}
          {image.status === "error" && (
            <span className="text-sm text-neutral-400">Image unavailable</span>
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
