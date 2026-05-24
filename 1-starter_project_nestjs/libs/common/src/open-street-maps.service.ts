import { Injectable } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import axios, { AxiosError } from 'axios';
import { IMapsService } from '@mytravels/contract';

const MAX_RETRIES = 2;
const DEFAULT_BASE_URL = 'https://nominatim.openstreetmap.org';
const DEFAULT_USER_AGENT = 'mytravels/1.0';

@Injectable()
export class OpenStreetMapsService implements IMapsService {
  private readonly baseUrl: string;
  private readonly userAgent: string;

  constructor(private readonly config: ConfigService) {
    this.baseUrl = config.get<string>('OPEN_STREET_MAPS_URL') ?? DEFAULT_BASE_URL;
    this.userAgent = config.get<string>('OPEN_STREET_MAPS_USER_AGENT') ?? DEFAULT_USER_AGENT;
  }

  async getAddress(latitude: number, longitude: number): Promise<string> {
    return this.withRetry(() =>
      axios.get(`${this.baseUrl}/reverse`, {
        params: { format: 'jsonv2', lat: latitude, lon: longitude },
        headers: { 'User-Agent': this.userAgent },
      }).then((res) => {
        if (res.data?.error) throw new Error(`OpenStreetMap geocode failed: ${res.data.error}`);
        const address = res.data?.display_name;
        if (!address) throw new Error('display_name missing in response');
        return address as string;
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
