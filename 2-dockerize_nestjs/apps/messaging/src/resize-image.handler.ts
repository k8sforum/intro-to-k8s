import { Injectable, Inject } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import { InjectRepository } from '@nestjs/typeorm';
import { Repository } from 'typeorm';
import * as sharp from 'sharp';
import { MessageSubscriberBase } from '@mytravels/common';
import { IObjectStorageService, ExchangeNames, BucketNames, PointOfInterestMessage } from '@mytravels/contract';
import { PointOfInterestEntity } from '@mytravels/domain';

@Injectable()
export class ResizeImageHandler extends MessageSubscriberBase<PointOfInterestMessage> {
  private static readonly mutex = new Set<number>();

  constructor(
    config: ConfigService,
    @InjectRepository(PointOfInterestEntity)
    private readonly poiRepo: Repository<PointOfInterestEntity>,
    @Inject(IObjectStorageService)
    private readonly storage: IObjectStorageService,
  ) {
    super(config, ExchangeNames.ResizeImage, ExchangeNames.ResizeImage);
  }

  protected async processMessage(message: PointOfInterestMessage): Promise<void> {
    if (ResizeImageHandler.mutex.has(message.pointOfInterestId)) return;
    ResizeImageHandler.mutex.add(message.pointOfInterestId);
    try {
      const point = await this.poiRepo.findOne({ where: { id: message.pointOfInterestId } });
      if (!point) throw new Error(`Point not found: ${message.pointOfInterestId}`);
      if (point.imageResized) return;

      const original = await this.storage.getStream(BucketNames.NewUploadedImagesContainer, point.generatedBlobName);
      const metadata = await sharp(original).metadata();
      const resized = await sharp(original)
        .resize(Math.round(metadata.width * 0.1), Math.round(metadata.height * 0.1))
        .jpeg()
        .toBuffer();

      await this.storage.saveStream(BucketNames.ResizedImagesContainer, point.generatedBlobName, resized, resized.length);

      point.imageResized = true;
      await this.poiRepo.save(point);
    } finally {
      ResizeImageHandler.mutex.delete(message.pointOfInterestId);
    }
  }
}
