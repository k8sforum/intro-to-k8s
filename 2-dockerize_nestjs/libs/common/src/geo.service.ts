import { Injectable } from '@nestjs/common';
import { IGeoService, GeoLocation } from '@mytravels/contract';
import * as exifr from 'exifr';

@Injectable()
export class GeoService implements IGeoService {
  async extractGeoLocation(buffer: Buffer): Promise<GeoLocation> {
    try {
      const data = await exifr.parse(buffer, { gps: true });
      if (data?.latitude && data?.longitude) {
        return { latitude: data.latitude, longitude: data.longitude };
      }
    } catch {
      // no GPS data in image
    }
    return { latitude: 0, longitude: 0 };
  }
}
