import {
  Entity, PrimaryGeneratedColumn, Column, ManyToOne, JoinColumn, CreateDateColumn,
} from 'typeorm';
import { PointOfInterestEntity } from './point-of-interest.entity';
import { TagEntity } from './tag.entity';

@Entity('PointOfInterestTagAssociations')
export class PointOfInterestTagAssociationEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'PointOfInterestId' })
  pointOfInterestId: number;

  @ManyToOne(() => PointOfInterestEntity, (p) => p.pointOfInterestTagAssociations)
  @JoinColumn({ name: 'PointOfInterestId' })
  pointOfInterest: PointOfInterestEntity;

  @Column({ name: 'TagId' })
  tagId: number;

  @ManyToOne(() => TagEntity)
  @JoinColumn({ name: 'TagId' })
  tag: TagEntity;

  @CreateDateColumn({ name: 'DateCreated' })
  dateCreated: Date;
}
