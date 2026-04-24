import {
  Entity, PrimaryGeneratedColumn, Column, ManyToOne, OneToMany,
  JoinColumn, CreateDateColumn,
} from 'typeorm';
import { PointOfInterestTypeEntity } from './point-of-interest-type.entity';
import { PointOfInterestStatusEntity } from './point-of-interest-status.entity';
import { PointOfInterestTagAssociationEntity } from './point-of-interest-tag-association.entity';
import { PointOfInterestAuditLogEntity } from './point-of-interest-audit-log.entity';

@Entity('PointOfInterests')
export class PointOfInterestEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'PointOfInterestKey', length: 40, nullable: true })
  pointOfInterestKey: string;

  @Column({ name: 'Container', length: 250, nullable: true })
  container: string;

  @Column({ name: 'OriginalFileName', length: 250, nullable: true })
  originalFileName: string;

  @Column({ name: 'GeneratedBlobName', length: 250, nullable: true })
  generatedBlobName: string;

  @Column({ name: 'Latitude', type: 'double precision' })
  latitude: number;

  @Column({ name: 'Longitude', type: 'double precision' })
  longitude: number;

  @CreateDateColumn({ name: 'DateCreated' })
  dateCreated: Date;

  @Column({ name: 'FormattedAddress', length: 300, nullable: true })
  formattedAddress: string;

  @Column({ name: 'ImageResized', default: false })
  imageResized: boolean = false;

  @Column({ name: 'PointOfInterestTypeId' })
  pointOfInterestTypeId: number;

  @ManyToOne(() => PointOfInterestTypeEntity)
  @JoinColumn({ name: 'PointOfInterestTypeId' })
  pointOfInterestType: PointOfInterestTypeEntity;

  @Column({ name: 'PointOfInterestStatusId' })
  pointOfInterestStatusId: number;

  @ManyToOne(() => PointOfInterestStatusEntity)
  @JoinColumn({ name: 'PointOfInterestStatusId' })
  pointOfInterestStatus: PointOfInterestStatusEntity;

  @OneToMany(() => PointOfInterestTagAssociationEntity, (a) => a.pointOfInterest)
  pointOfInterestTagAssociations: PointOfInterestTagAssociationEntity[];

  @OneToMany(() => PointOfInterestAuditLogEntity, (a) => a.pointOfInterest)
  pointOfInterestAuditLogs: PointOfInterestAuditLogEntity[];

  @Column({ name: 'DateUpdated', nullable: true })
  dateUpdated: Date;

  @Column({ name: 'UpdatedBy', nullable: true })
  updatedBy: string;

  @Column({ name: 'Reason', length: 500, nullable: true })
  reason: string;
}
