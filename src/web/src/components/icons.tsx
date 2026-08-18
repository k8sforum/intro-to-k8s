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
