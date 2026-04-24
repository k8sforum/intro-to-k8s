import { Injectable, Inject } from '@nestjs/common';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import { DataSource, Repository } from 'typeorm';
import { v4 as uuidv4 } from 'uuid';
import {
  IPointOfInterestService, IObjectStorageService, IMessagePublisher, IGeoService,
  BucketNames, ExchangeNames, PointOfInterestTypesEnum, PointOfInterestStatusesEnum,
  CreatePointOfInterestDto, GetPointOfInterestResponse, EntityNotFoundException,
  PointOfInterestMessage,
} from '@mytravels/contract';
import { PointOfInterestEntity } from '../entities/point-of-interest.entity';

@Injectable()
export class PointOfInterestService implements IPointOfInterestService {
  constructor(
    @InjectRepository(PointOfInterestEntity)
    private readonly poiRepo: Repository<PointOfInterestEntity>,
    @InjectDataSource()
    private readonly dataSource: DataSource,
    @Inject(IObjectStorageService)
    private readonly storage: IObjectStorageService,
    @Inject(IMessagePublisher)
    private readonly publisher: IMessagePublisher,
    @Inject(IGeoService)
    private readonly geoService: IGeoService,
  ) {}

  async getAll(): Promise<GetPointOfInterestResponse[]> {
    return this.dataSource.query('SELECT * FROM public.spgetpointofinterest()');
  }

  async getByTag(tagName: string): Promise<GetPointOfInterestResponse[]> {
    return this.dataSource.query('SELECT * FROM public.spgetpointofinterestbytagname($1)', [tagName]);
  }

  async saveFileAsPointOfInterest(file: Express.Multer.File): Promise<number> {
    const objectName = await this.storage.saveFile(file, BucketNames.NewUploadedImagesContainer);
    const imageBuffer = await this.storage.getStream(BucketNames.NewUploadedImagesContainer, objectName);
    const geo = await this.geoService.extractGeoLocation(imageBuffer);

    if (!geo.latitude || !geo.longitude) {
      throw new Error('Image is not geocoded');
    }

    const dto: CreatePointOfInterestDto = {
      originalFileName: file.originalname,
      blobName: objectName,
      formattedAddress: '',
      latitude: geo.latitude,
      longitude: geo.longitude,
      pointOfInterestTypeId: PointOfInterestTypesEnum.Image,
    };

    const entity = this.toEntity(dto);
    const saved = await this.poiRepo.save(entity);

    const msg: PointOfInterestMessage = { correlationId: uuidv4(), pointOfInterestId: saved.id };
    await this.publisher.publish(ExchangeNames.AppendFormattedAddress, msg);
    await this.publisher.publish(ExchangeNames.ResizeImage, { correlationId: '', pointOfInterestId: saved.id });

    return saved.id;
  }

  async updatePointOfInterest(file: Express.Multer.File, pointOfInterestKey: string): Promise<number> {
    const objectName = await this.storage.saveFile(file, BucketNames.NewUploadedImagesContainer);
    const existing = await this.poiRepo.findOne({ where: { pointOfInterestKey }, order: { dateCreated: 'DESC' } });
    if (!existing) throw new EntityNotFoundException(pointOfInterestKey);

    const newRow = new PointOfInterestEntity();
    newRow.pointOfInterestKey = existing.pointOfInterestKey;
    newRow.container = BucketNames.NewUploadedImagesContainer;
    newRow.originalFileName = file.originalname;
    newRow.generatedBlobName = objectName;
    newRow.latitude = existing.latitude;
    newRow.longitude = existing.longitude;
    newRow.formattedAddress = existing.formattedAddress;
    newRow.imageResized = false;
    newRow.pointOfInterestTypeId = existing.pointOfInterestTypeId;
    newRow.pointOfInterestStatusId = existing.pointOfInterestStatusId;
    const saved = await this.poiRepo.save(newRow);

    await this.publisher.publish(ExchangeNames.ResizeImage, { correlationId: uuidv4(), pointOfInterestId: saved.id });
    return saved.id;
  }

  async updateStatus(pointOfInterestKey: string, statusId: number): Promise<number> {
    const existing = await this.poiRepo.findOne({ where: { pointOfInterestKey }, order: { dateCreated: 'DESC' } });
    if (!existing) throw new EntityNotFoundException(pointOfInterestKey);

    const newRow = new PointOfInterestEntity();
    newRow.pointOfInterestKey = existing.pointOfInterestKey;
    newRow.container = existing.container;
    newRow.originalFileName = existing.originalFileName;
    newRow.generatedBlobName = existing.generatedBlobName;
    newRow.latitude = existing.latitude;
    newRow.longitude = existing.longitude;
    newRow.formattedAddress = existing.formattedAddress;
    newRow.imageResized = existing.imageResized;
    newRow.pointOfInterestTypeId = existing.pointOfInterestTypeId;
    newRow.pointOfInterestStatusId = statusId;
    newRow.dateUpdated = new Date();
    const saved = await this.poiRepo.save(newRow);
    return saved.id;
  }

  private toEntity(dto: CreatePointOfInterestDto): PointOfInterestEntity {
    const entity = new PointOfInterestEntity();
    entity.originalFileName = dto.originalFileName;
    entity.generatedBlobName = dto.blobName;
    entity.container = BucketNames.NewUploadedImagesContainer;
    entity.dateCreated = new Date();
    entity.latitude = dto.latitude;
    entity.longitude = dto.longitude;
    entity.pointOfInterestTypeId = dto.pointOfInterestTypeId;
    entity.formattedAddress = dto.formattedAddress;
    entity.pointOfInterestKey = uuidv4();
    entity.pointOfInterestStatusId = PointOfInterestStatusesEnum.Open;
    return entity;
  }
}
