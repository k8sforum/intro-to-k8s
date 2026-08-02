import { useCallback, useEffect, useRef, useState } from 'react';
import { getPointsOfInterest } from './api/client';
import type { PointOfInterest } from './api/types';
import { hasCoordinates } from './api/types';
import { MapView } from './components/MapView';
import { PoiDialog } from './components/PoiDialog';
import { UploadButton } from './components/UploadButton';

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

  function handleUploaded() {
    pollUntilResolved(pois.length);
  }

  return (
    <div className="relative h-svh w-full">
      <header className="absolute top-4 right-5 z-[500]">
        <h1 className="rounded-full bg-white/90 px-4 py-1.5 text-sm font-medium text-neutral-800 shadow dark:bg-neutral-900/90 dark:text-neutral-100">
          MyTravels POI Viewer
        </h1>
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
