import { DataSource } from 'typeorm';
import { PointOfInterestEntity } from './entities/point-of-interest.entity';
import { PointOfInterestAuditLogEntity } from './entities/point-of-interest-audit-log.entity';
import { PointOfInterestStatusEntity } from './entities/point-of-interest-status.entity';
import { PointOfInterestTagAssociationEntity } from './entities/point-of-interest-tag-association.entity';
import { PointOfInterestTypeEntity } from './entities/point-of-interest-type.entity';
import { TagEntity } from './entities/tag.entity';

export const AppDataSource = new DataSource({
  type: 'postgres',
  url: process.env.DATABASE_URL,
  entities: [
    PointOfInterestEntity,
    PointOfInterestAuditLogEntity,
    PointOfInterestStatusEntity,
    PointOfInterestTagAssociationEntity,
    PointOfInterestTypeEntity,
    TagEntity,
  ],
  migrations: ['libs/domain/src/migrations/*.ts'],
  migrationsTableName: 'typeorm_migrations',
});
