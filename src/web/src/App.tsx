import { useCallback, useEffect, useRef, useState } from 'react';
import { getPointsOfInterest } from './api/client';
import type { PointOfInterest } from './api/types';
import { hasCoordinates } from './api/types';
import { MapView } from './components/MapView';
import { PoiDialog } from './components/PoiDialog';
import { UploadButton } from './components/UploadButton';
import { PostmarkGlyph } from './components/icons';

const POLL_INTERVAL_MS = 3000;
const POLL_MAX_ATTEMPTS = 10;

function App() {
  const [pois, setPois] = useState<PointOfInterest[]>([]);
  const [selectedPoi, setSelectedPoi] = useState<PointOfInterest | null>(null);
  const pollTimer = useRef<ReturnType<typeof setInterval> | null>(null);

  const refresh = useCallback(async () => {
    const data = await getPointsOfInterest();
    setPois(data);
    return data;
  }, []);

  useEffect(() => {
    refresh().catch(() => {
      /* surfaced via empty map; swagger/CORS is the usual culprit during setup */
    });
    return () => {
      if (pollTimer.current) clearInterval(pollTimer.current);
    };
  }, [refresh]);

  function pollUntilResolved(countBeforeUpload: number) {
    if (pollTimer.current) clearInterval(pollTimer.current);
    let attempts = 0;

    pollTimer.current = setInterval(async () => {
      attempts += 1;
      const data = await refresh().catch(() => null);

      const newCount = data ? data.length - countBeforeUpload : 0;
      const newestFirst = data ? [...data].sort((a, b) => b.id - a.id) : [];
      const newlyAddedPois = newestFirst.slice(0, Math.max(newCount, 0));
      const resolved = newCount > 0 && newlyAddedPois.every(hasCoordinates);

      if (resolved || attempts >= POLL_MAX_ATTEMPTS) {
        if (pollTimer.current) clearInterval(pollTimer.current);
        pollTimer.current = null;
      }
    }, POLL_INTERVAL_MS);
  }

  async function handleUploaded() {
    const countBeforeUpload = pois.length;
    await refresh();
    pollUntilResolved(countBeforeUpload);
  }

  return (
    <div className="relative h-svh w-full">
      <header className="absolute top-4 right-4 z-[500] sm:top-5 sm:right-5">
        <div className="flex items-center gap-2.5 rounded-md border border-brass/40 bg-paper/95 px-3.5 py-2 shadow-md backdrop-blur-sm dark:border-brass/30 dark:bg-harbor-2/95">
          <PostmarkGlyph className="h-4.5 w-4.5 shrink-0 text-postmark dark:text-postmark-light" />
          <h1 className="leading-tight">
            <span className="block font-display text-[15px] font-medium tracking-tight text-ink italic dark:text-bone">
              MyTravels
            </span>
            <span className="block font-mono text-[9px] tracking-[0.18em] text-brass uppercase">
              Field Log
            </span>
          </h1>
        </div>
      </header>

      <MapView pois={pois} onSelect={setSelectedPoi} />

      <div className="absolute right-5 bottom-5 z-[500]">
        <UploadButton onUploaded={handleUploaded} />
      </div>

      {selectedPoi && <PoiDialog poi={selectedPoi} onClose={() => setSelectedPoi(null)} />}
    </div>
  );
}

export default App;
