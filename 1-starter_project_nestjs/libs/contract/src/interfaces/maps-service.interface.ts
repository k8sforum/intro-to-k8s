export interface IMapsService {
  getAddress(latitude: number, longitude: number): Promise<string>;
}

export const IMapsService = Symbol('IMapsService');
