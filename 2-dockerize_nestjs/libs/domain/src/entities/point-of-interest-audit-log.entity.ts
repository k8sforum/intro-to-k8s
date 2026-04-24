import {
  Entity, PrimaryGeneratedColumn, Column, ManyToOne, JoinColumn, CreateDateColumn,
} from 'typeorm';
import { PointOfInterestEntity } from './point-of-interest.entity';

@Entity('PointOfInterestAuditLogs')
export class PointOfInterestAuditLogEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'QueueName', length: 100, nullable: true })
  queueName: string;

  @Column({ name: 'Payload', length: 500, nullable: true })
  payload: string;

  @Column({ name: 'Sucessful', default: false })
  sucessful: boolean = false;

  @Column({ name: 'ErrorMessage', length: 500, nullable: true })
  errorMessage: string;

  @Column({ name: 'PointOfInterestId' })
  pointOfInterestId: number;

  @ManyToOne(() => PointOfInterestEntity, (p) => p.pointOfInterestAuditLogs)
  @JoinColumn({ name: 'PointOfInterestId' })
  pointOfInterest: PointOfInterestEntity;

  @CreateDateColumn({ name: 'DateCreated' })
  dateCreated: Date;
}
