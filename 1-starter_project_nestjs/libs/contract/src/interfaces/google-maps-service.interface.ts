export interface IGoogleMapsService {
  getAddress(latitude: number, longitude: number): Promise<string>;
}

export const IGoogleMapsService = Symbol('IGoogleMapsService');
