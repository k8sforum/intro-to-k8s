export interface Tag {
  id: number;
  name: string;
  dateCreated: string;
}

export interface PointOfInterest {
  id: number;
  pointOfInterestKey: string;
  latitude: number;
  longitude: number;
  pointOfInterestTypeId: number;
  pointOfInterestType: string;
  pointOfInterestStatusId: number;
  pointOfInterestStatus: string;
  primaryColor: string;
  secondaryColor: string;
  dateCreated: string;
  dateTaken: string | null;
  formattedAddress: string;
  tags: Tag[];
}

export interface SaveEntityResponse {
  id: number;
}

export function hasCoordinates(poi: PointOfInterest): boolean {
  return poi.latitude !== 0 || poi.longitude !== 0;
}
