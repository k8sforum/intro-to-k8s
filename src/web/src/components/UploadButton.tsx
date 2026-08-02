import { useRef, useState } from 'react';
import { uploadPointOfInterestImage } from '../api/client';

type UploadState = 'idle' | 'uploading' | 'error';

interface UploadButtonProps {
  onUploaded: () => void;
}

export function UploadButton({ onUploaded }: UploadButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [state, setState] = useState<UploadState>('idle');
  const [error, setError] = useState<string | null>(null);

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;

    setState('uploading');
    setError(null);
    try {
      await uploadPointOfInterestImage(file);
      setState('idle');
      onUploaded();
    } catch {
      setState('error');
      setError(
        'Upload failed. Make sure the image has GPS location data (EXIF) and try again.',
      );
    }
  }

  return (
    <div className="flex flex-col items-end gap-2">
      {error && (
        <p className="max-w-64 rounded-md bg-red-50 px-3 py-2 text-xs text-red-700 shadow dark:bg-red-950 dark:text-red-300">
          {error}
        </p>
      )}
      <button
        type="button"
        disabled={state === 'uploading'}
        onClick={() => inputRef.current?.click()}
        className="rounded-full bg-neutral-900 px-4 py-2 text-sm font-medium text-white shadow-md transition hover:bg-neutral-700 disabled:cursor-not-allowed disabled:opacity-60 dark:bg-white dark:text-neutral-900 dark:hover:bg-neutral-200"
      >
        {state === 'uploading' ? 'Uploading…' : 'Upload Image'}
      </button>
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={handleFileChange}
      />
    </div>
  );
}
