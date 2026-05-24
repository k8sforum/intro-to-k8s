import { Module } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { IGeoService, IMapsService, IMessagePublisher } from '@mytravels/contract';
import { GeoService } from './geo.service';
import { GoogleMapsService } from './google-maps.service';
import { OpenStreetMapsService } from './open-street-maps.service';
import { MessagePublisherService } from './message-publisher.service';

@Module({
  providers: [
    { provide: IGeoService, useClass: GeoService },
    GoogleMapsService,
    OpenStreetMapsService,
    {
      provide: IMapsService,
      inject: [ConfigService, GoogleMapsService, OpenStreetMapsService],
      useFactory: (
        config: ConfigService,
        google: GoogleMapsService,
        osm: OpenStreetMapsService,
      ) => {
        const apiKey = config.get<string>('GOOGLE_API_KEY');
        return !apiKey || apiKey === '<YOUR_GOOGLE_API_KEY>' ? osm : google;
      },
    },
    { provide: IMessagePublisher, useClass: MessagePublisherService },
  ],
  exports: [IGeoService, IMapsService, IMessagePublisher],
})
export class CommonModule {}
