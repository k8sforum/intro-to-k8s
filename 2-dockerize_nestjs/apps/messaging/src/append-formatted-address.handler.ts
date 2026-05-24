import { Injectable, Inject } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm';
import { MessageSubscriberBase } from '@mytravels/common';
import { IMapsService, ExchangeNames, PointOfInterestMessage } from '@mytravels/contract';
import { PointOfInterestEntity } from '@mytravels/domain';

@Injectable()
export class AppendFormattedAddressHandler extends MessageSubscriberBase<PointOfInterestMessage> {
  private static readonly mutex = new Set<number>();

  constructor(
    config: ConfigService,
    @InjectRepository(PointOfInterestEntity)
    private readonly poiRepo: Repository<PointOfInterestEntity>,
    @Inject(IMapsService)
    private readonly maps: IMapsService,
  ) {
    super(config, ExchangeNames.AppendFormattedAddress, ExchangeNames.AppendFormattedAddress);
  }

  protected async processMessage(message: PointOfInterestMessage): Promise<void> {
    if (AppendFormattedAddressHandler.mutex.has(message.pointOfInterestId)) return;
    AppendFormattedAddressHandler.mutex.add(message.pointOfInterestId);
    try {
      const point = await this.poiRepo.findOne({ where: { id: message.pointOfInterestId } });
      if (!point) throw new Error(`Point not found: ${message.pointOfInterestId}`);

      if (!point.formattedAddress?.trim()) {
        point.formattedAddress = await this.maps.getAddress(point.latitude, point.longitude);
        await this.poiRepo.save(point);
      }
    } finally {
      AppendFormattedAddressHandler.mutex.delete(message.pointOfInterestId);
    }
  }
}
