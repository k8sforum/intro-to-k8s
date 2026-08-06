import { useEffect, useRef, useState } from 'react';
import {
  uploadPointOfInterestImage,
  uploadPointOfInterestImageAtPlace,
} from '../api/client';
import type { Place } from '../api/types';
import { LocationSearchDialog } from './LocationSearchDialog';
import { Spinner } from './Spinner';

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
        <p className="max-w-64 rounded-md bg-red-50 px-3 py-2 text-xs text-red-700 shadow dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}

      {menuOpen && (
        <div className="absolute right-0 bottom-full mb-2 w-64 overflow-hidden rounded-md bg-white shadow-lg dark:bg-neutral-800">
          <button
            type="button"
            onClick={() => pickFile('search')}
            className="w-full px-4 py-3 text-left text-sm text-neutral-700 transition hover:bg-neutral-100 dark:text-neutral-200 dark:hover:bg-neutral-700"
          >
            Upload without location data
            <span className="mt-0.5 block text-xs text-neutral-500 dark:text-neutral-400">
              Search for the place instead
            </span>
          </button>
        </div>
      )}

      <div className="flex items-stretch overflow-hidden rounded-full bg-neutral-900 shadow-md dark:bg-white">
        <button
          type="button"
          disabled={uploading}
          onClick={() => pickFile('gps')}
          className="px-4 py-2 text-sm font-medium text-white transition hover:bg-neutral-700 disabled:cursor-not-allowed disabled:opacity-60 dark:text-neutral-900 dark:hover:bg-neutral-200"
        >
          {uploading ? (
            <span className="flex items-center gap-2">
              <Spinner className="h-4 w-4" />
              Uploading…
            </span>
          ) : (
            'Upload Image'
          )}
        </button>
        <span className="my-2 w-px bg-white/25 dark:bg-neutral-900/25" aria-hidden="true" />
        <button
          type="button"
          disabled={uploading}
          onClick={() => setMenuOpen((open) => !open)}
          aria-label="More upload options"
          aria-expanded={menuOpen}
          aria-haspopup="menu"
          className="px-3 py-2 text-white transition hover:bg-neutral-700 disabled:cursor-not-allowed disabled:opacity-60 dark:text-neutral-900 dark:hover:bg-neutral-200"
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
