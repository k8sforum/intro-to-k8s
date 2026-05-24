import { Injectable, OnModuleInit, Logger, Inject } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository, MoreThan, IsNull } from 'typeorm';
import { IMapsService } from '@mytravels/contract';
import { PointOfInterestEntity } from '@mytravels/domain';

const INTERVAL_MS = 30 * 60 * 1000;
const TWO_DAYS_MS = 2 * 24 * 60 * 60 * 1000;

@Injectable()
export class AppendFormattedAddressSweeper implements OnModuleInit {
  private readonly logger = new Logger(AppendFormattedAddressSweeper.name);

  constructor(
    @InjectRepository(PointOfInterestEntity)
    private readonly poiRepo: Repository<PointOfInterestEntity>,
    @Inject(IMapsService)
    private readonly maps: IMapsService,
  ) {}

  async onModuleInit(): Promise<void> {
    await this.doWork();
    setInterval(() => this.doWork(), INTERVAL_MS);
  }

  private async doWork(): Promise<void> {
    try {
      const twoDaysAgo = new Date(Date.now() - TWO_DAYS_MS);
      const pois = await this.poiRepo.find({
        where: [
          { formattedAddress: '', dateCreated: MoreThan(twoDaysAgo) },
          { formattedAddress: IsNull(), dateCreated: MoreThan(twoDaysAgo) },
        ],
      });

      for (const poi of pois) {
        try {
          poi.formattedAddress = await this.maps.getAddress(poi.latitude, poi.longitude);
          await this.poiRepo.save(poi);
        } catch (err) {
          this.logger.error(`Sweeper failed for POI ${poi.id}`, err);
        }
      }
    } catch (err) {
      this.logger.error('Sweeper execution failed', err);
    }
  }
}
