import { Module } from '@nestjs/common';
import { CoreDbModule, PointOfInterestService } from '@mytravels/domain';
import { CommonModule } from '@mytravels/common';
import { StorageModule } from '@mytravels/storage';
import { IPointOfInterestService } from '@mytravels/contract';
import { PointOfInterestController } from './point-of-interest.controller';

@Module({
  imports: [CoreDbModule, CommonModule, StorageModule],
  controllers: [PointOfInterestController],
  providers: [
    { provide: IPointOfInterestService, useClass: PointOfInterestService },
  ],
})
export class PointOfInterestModule {}
