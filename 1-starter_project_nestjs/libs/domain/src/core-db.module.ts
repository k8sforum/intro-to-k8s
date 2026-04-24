import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';
import { ConfigService } from '@nestjs/config';
import { PointOfInterestEntity } from './entities/point-of-interest.entity';
import { PointOfInterestAuditLogEntity } from './entities/point-of-interest-audit-log.entity';
import { PointOfInterestStatusEntity } from './entities/point-of-interest-status.entity';
import { PointOfInterestTagAssociationEntity } from './entities/point-of-interest-tag-association.entity';
import { PointOfInterestTypeEntity } from './entities/point-of-interest-type.entity';
import { TagEntity } from './entities/tag.entity';

@Module({
  imports: [
    TypeOrmModule.forRootAsync({
      inject: [ConfigService],
      useFactory: (config: ConfigService) => ({
        type: 'postgres',
        url: config.get<string>('DATABASE_URL'),
        entities: [
          PointOfInterestEntity,
          PointOfInterestAuditLogEntity,
          PointOfInterestStatusEntity,
          PointOfInterestTagAssociationEntity,
          PointOfInterestTypeEntity,
          TagEntity,
        ],
        synchronize: false,
      }),
    }),
    TypeOrmModule.forFeature([
      PointOfInterestEntity,
      PointOfInterestAuditLogEntity,
      PointOfInterestStatusEntity,
      PointOfInterestTagAssociationEntity,
      PointOfInterestTypeEntity,
      TagEntity,
    ]),
  ],
  providers: [],
  exports: [
    TypeOrmModule,
  ],
})
export class CoreDbModule {}
