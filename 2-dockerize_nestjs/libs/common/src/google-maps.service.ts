import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import axios, { AxiosError } from 'axios';
import { IMapsService } from '@mytravels/contract';

const MAX_RETRIES = 2;

@Injectable()
export class GoogleMapsService implements IMapsService {
  private readonly apiKey: string;
  private readonly baseUrl: string;

  constructor(private readonly config: ConfigService) {
    this.apiKey = config.get<string>('GOOGLE_API_KEY');
    this.baseUrl = config.get<string>('GOOGLE_MAPS_URL');
  }

  async getAddress(latitude: number, longitude: number): Promise<string> {
    const latlong = `${latitude},${longitude}`;
    return this.withRetry(() =>
      axios.get(`${this.baseUrl}/maps/api/geocode/json`, {
        params: { latlng: latlong, key: this.apiKey },
      }).then((res) => {
        const results = res.data?.results;
        if (!results?.length) throw new Error('Google Maps API returned no results');
        return results[0].formatted_address as string;
      }),
    );
  }

  private async withRetry<T>(fn: () => Promise<T>, attempt = 0): Promise<T> {
    try {
      return await fn();
    } catch (err) {
      if (attempt < MAX_RETRIES && err instanceof AxiosError) {
        const delay = Math.pow(2, attempt + 1) * 1000;
        await new Promise((r) => setTimeout(r, delay));
        return this.withRetry(fn, attempt + 1);
      }
      throw err;
    }
  }
}
