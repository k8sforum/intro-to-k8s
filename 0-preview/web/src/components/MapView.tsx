import { MapContainer, Marker, TileLayer, useMap } from 'react-leaflet';
import { useEffect } from 'react';
import type { PointOfInterest } from '../api/types';
import { hasCoordinates } from '../api/types';
import { defaultIcon } from './leafletIcon';

const DEFAULT_CENTER: [number, number] = [20, 0];
const DEFAULT_ZOOM = 2;
const FOCUSED_ZOOM = 13;

interface MapViewProps {
  pois: PointOfInterest[];
  onSelect: (poi: PointOfInterest) => void;
}

function FitToMarkers({ pois }: { pois: PointOfInterest[] }) {
  const map = useMap();

  useEffect(() => {
    const located = pois.filter(hasCoordinates);
    if (located.length === 0) return;
    if (located.length === 1) {
      map.setView([located[0].latitude, located[0].longitude], FOCUSED_ZOOM);
      return;
    }
    map.fitBounds(
      located.map((poi) => [poi.latitude, poi.longitude] as [number, number]),
      { padding: [40, 40] },
    );
  }, [pois, map]);

  return null;
}

export function MapView({ pois, onSelect }: MapViewProps) {
  const located = pois.filter(hasCoordinates);

  return (
    <MapContainer
      center={DEFAULT_CENTER}
      zoom={DEFAULT_ZOOM}
      className="h-full w-full"
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      <FitToMarkers pois={pois} />
      {located.map((poi) => (
        <Marker
          key={poi.id}
          position={[poi.latitude, poi.longitude]}
          icon={defaultIcon}
          eventHandlers={{ click: () => onSelect(poi) }}
        />
      ))}
    </MapContainer>
  );
}
