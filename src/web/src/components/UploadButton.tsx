import { useEffect, useRef, useState } from 'react';
import {
  uploadPointOfInterestImage,
  uploadPointOfInterestImageAtPlace,
} from '../api/client';
import type { Place } from '../api/types';
import { LocationSearchDialog } from './LocationSearchDialog';
import { Spinner } from './Spinner';
import { PostmarkGlyph } from './icons';

type UploadState = 'idle' | 'uploading' | 'error';

/** 'gps' reads the coordinates from the image, 'search' asks the user for a location. */
type UploadMode = 'gps' | 'search';

interface UploadButtonProps {
  onUploaded: () => Promise<void>;
}

export function UploadButton({ onUploaded }: UploadButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const modeRef = useRef<UploadMode>('gps');
  const [state, setState] = useState<UploadState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  const [pendingFile, setPendingFile] = useState<File | null>(null);

  useEffect(() => {
    if (!menuOpen) return;

    function handlePointerDown(e: MouseEvent) {
      if (!containerRef.current?.contains(e.target as Node)) setMenuOpen(false);
    }

    document.addEventListener('mousedown', handlePointerDown);
    return () => document.removeEventListener('mousedown', handlePointerDown);
  }, [menuOpen]);

  async function upload(send: () => Promise<unknown>, failureMessage: string) {
    setState('uploading');
    setError(null);
    try {
      await send();
      await onUploaded();
      setState('idle');
    } catch {
      setState('error');
      setError(failureMessage);
    }
  }

  function pickFile(mode: UploadMode) {
    modeRef.current = mode;
    setMenuOpen(false);
    inputRef.current?.click();
  }

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;

    if (modeRef.current === 'search') {
      // Hold the file until the user has picked a location for it.
      setPendingFile(file);
      return;
    }

    upload(
      () => uploadPointOfInterestImage(file),
      'Upload failed. Make sure the image has GPS location data (EXIF) and try again.',
    );
  }

  function handlePlaceSelected(place: Place) {
    const file = pendingFile;
    setPendingFile(null);
    if (!file) return;

    upload(
      () => uploadPointOfInterestImageAtPlace(file, place),
      'Upload failed. Please try again.',
    );
  }

  const uploading = state === 'uploading';

  return (
    <div ref={containerRef} className="relative flex flex-col items-end gap-2">
      {error && (
        <p className="max-w-64 rounded-md border border-postmark/40 bg-postmark/10 px-3 py-2 font-sans text-xs text-postmark shadow dark:border-postmark-light/40 dark:bg-postmark-light/10 dark:text-postmark-light">
          {error}
        </p>
      )}

      {menuOpen && (
        <div className="absolute right-0 bottom-full mb-2 w-64 overflow-hidden rounded-md border border-brass/30 bg-paper shadow-lg dark:border-brass/25 dark:bg-harbor-2">
          <button
            type="button"
            onClick={() => pickFile('search')}
            className="w-full px-4 py-3 text-left font-sans text-sm text-ink transition hover:bg-brass/10 dark:text-bone dark:hover:bg-brass/10"
          >
            No GPS on this photo?
            <span className="mt-0.5 block text-xs text-ink/60 dark:text-bone/60">
              Search for the place instead
            </span>
          </button>
        </div>
      )}

      <div className="flex items-stretch overflow-hidden rounded-md border border-brass/40 bg-ink shadow-md dark:border-brass/30 dark:bg-harbor-2">
        <button
          type="button"
          disabled={uploading}
          onClick={() => pickFile('gps')}
          className="px-4 py-2.5 font-sans text-sm font-medium text-bone transition hover:bg-ink-2 disabled:cursor-not-allowed disabled:opacity-60 dark:hover:bg-harbor"
        >
          {uploading ? (
            <span className="flex items-center gap-2">
              <Spinner className="h-4 w-4" />
              Stamping…
            </span>
          ) : (
            <span className="flex items-center gap-2">
              <PostmarkGlyph className="h-4 w-4 text-postmark-light" />
              Log a photo
            </span>
          )}
        </button>
        <span className="my-2 w-px bg-brass/30" aria-hidden="true" />
        <button
          type="button"
          disabled={uploading}
          onClick={() => setMenuOpen((open) => !open)}
          aria-label="More upload options"
          aria-expanded={menuOpen}
          aria-haspopup="menu"
          className="px-3 py-2 text-bone transition hover:bg-ink-2 disabled:cursor-not-allowed disabled:opacity-60 dark:hover:bg-harbor"
        >
          <svg
            className={`h-4 w-4 transition-transform ${menuOpen ? 'rotate-180' : ''}`}
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            aria-hidden="true"
          >
            <path d="M6 9l6 6 6-6" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        </button>
      </div>

      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={handleFileChange}
      />

      {pendingFile && (
        <LocationSearchDialog
          fileName={pendingFile.name}
          onSelect={handlePlaceSelected}
          onClose={() => setPendingFile(null)}
        />
      )}
    </div>
  );
}
