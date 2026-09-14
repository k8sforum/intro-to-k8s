interface PostmarkGlyphProps {
  className?: string;
}

/** The stamp/cancellation-mark motif used for map pins, the wordmark and the date badge. */
export function PostmarkGlyph({ className = 'h-4 w-4' }: PostmarkGlyphProps) {
  return (
    <svg viewBox="0 0 28 28" className={className} fill="none" aria-hidden="true">
      <circle cx="14" cy="14" r="9.5" stroke="currentColor" strokeWidth="2" />
      <circle cx="14" cy="14" r="2.75" fill="currentColor" />
      <line x1="14" y1="1" x2="14" y2="3.5" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <line x1="14" y1="24.5" x2="14" y2="27" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <line x1="1" y1="14" x2="3.5" y2="14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <line x1="24.5" y1="14" x2="27" y2="14" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
    </svg>
  );
}

/** A magnifier, used for the map's search control. */
export function SearchGlyph({ className = 'h-4 w-4' }: PostmarkGlyphProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" aria-hidden="true">
      <circle cx="10.5" cy="10.5" r="6.5" stroke="currentColor" strokeWidth="2" />
      <line
        x1="15.5"
        y1="15.5"
        x2="20"
        y2="20"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

/** A short connected path of stops, used to link to a point's message trace. */
export function TraceGlyph({ className = 'h-4 w-4' }: PostmarkGlyphProps) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="none" aria-hidden="true">
      <path
        d="M4 18c3-1 3-5 6-6s3-5 6-6 4 3 4 3"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinecap="round"
      />
      <circle cx="4" cy="18" r="1.5" fill="currentColor" />
      <circle cx="10" cy="12" r="1.5" fill="currentColor" />
      <circle cx="16" cy="6" r="1.5" fill="currentColor" />
      <circle cx="20" cy="9" r="1.5" fill="currentColor" />
    </svg>
  );
}
