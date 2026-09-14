import { useCallback, useEffect, useRef, useState } from 'react';
import { useBooleanFlagValue } from '@openfeature/react-sdk';
import { getPointsOfInterest } from '../api/client';
import type { PointOfInterest } from '../api/types';
import { hasCoordinates } from '../api/types';
import { MapSearchBox } from './MapSearchBox';
import { MapView } from './MapView';
import { PoiDialog } from './PoiDialog';
import { UploadButton } from './UploadButton';

const POLL_INTERVAL_MS = 3000;
const POLL_MAX_ATTEMPTS = 10;

export function MapPage() {
  const [allPois, setAllPois] = useState<PointOfInterest[]>([]);
  // Null means no search is narrowing the map, so every known point is shown.
  const [results, setResults] = useState<PointOfInterest[] | null>(null);
  const [selectedPoi, setSelectedPoi] = useState<PointOfInterest | null>(null);
  const pollTimer = useRef<ReturnType<typeof setInterval> | null>(null);
  const allPoisRef = useRef<PointOfInterest[]>([]);
  const searchEnabled = useBooleanFlagValue('enable-poi-search', true);

  const pois = results ?? allPois;

  // The upload poll counts against the full library, never against a search-narrowed view, so the
  // refresh always writes to allPois and the displayed results are left alone while a search is up.
  const refresh = useCallback(async () => {
    const data = await getPointsOfInterest();
    allPoisRef.current = data;
    setAllPois(data);
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

      // The list is served from SOLR now, so this waits on the index-solr message being consumed
      // as well as on the enrichment. Giving up after POLL_MAX_ATTEMPTS leaves the point to appear
      // on the next load rather than blocking the page.
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
    const countBeforeUpload = allPoisRef.current.length;
    await refresh();
    pollUntilResolved(countBeforeUpload);
  }

  return (
    <>
      <MapView pois={pois} onSelect={setSelectedPoi} />

      {searchEnabled && (
        <div className="absolute bottom-5 left-5 z-[500]">
          <MapSearchBox onResults={setResults} />
        </div>
      )}

      <div className="absolute right-5 bottom-5 z-[500]">
        <UploadButton onUploaded={handleUploaded} />
      </div>

      {selectedPoi && <PoiDialog poi={selectedPoi} onClose={() => setSelectedPoi(null)} />}
    </>
  );
}
