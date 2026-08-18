import L from 'leaflet';

const PIN_SIZE = 28;

/** Each visited place is stamped onto the map like a postmark rather than dropped with a pin. */
export const defaultIcon = L.divIcon({
  className: 'mt-pin',
  html: `
    <svg width="${PIN_SIZE}" height="${PIN_SIZE}" viewBox="0 0 28 28" class="text-postmark dark:text-postmark-light drop-shadow-sm">
      <circle cx="14" cy="14" r="9.5" fill="none" stroke="currentColor" stroke-width="2" />
      <circle cx="14" cy="14" r="2.75" fill="currentColor" />
      <line x1="14" y1="1" x2="14" y2="3.5" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
      <line x1="14" y1="24.5" x2="14" y2="27" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
      <line x1="1" y1="14" x2="3.5" y2="14" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
      <line x1="24.5" y1="14" x2="27" y2="14" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
    </svg>
  `,
  iconSize: [PIN_SIZE, PIN_SIZE],
  iconAnchor: [PIN_SIZE / 2, PIN_SIZE / 2],
});
