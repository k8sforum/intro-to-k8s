export interface GeoLocation {
  latitude: number;
  longitude: number;
}

export interface IGeoService {
  extractGeoLocation(buffer: Buffer): Promise<GeoLocation>;
}

export const IGeoService = Symbol('IGeoService');
