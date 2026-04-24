import { Module } from '@nestjs/common';
import { IGeoService, IGoogleMapsService, IMessagePublisher } from '@mytravels/contract';
import { GeoService } from './geo.service';
import { GoogleMapsService } from './google-maps.service';
import { MessagePublisherService } from './message-publisher.service';

@Module({
  providers: [
    { provide: IGeoService, useClass: GeoService },
    { provide: IGoogleMapsService, useClass: GoogleMapsService },
    { provide: IMessagePublisher, useClass: MessagePublisherService },
  ],
  exports: [IGeoService, IGoogleMapsService, IMessagePublisher],
})
export class CommonModule {}
