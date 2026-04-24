import { Entity, PrimaryGeneratedColumn, Column } from 'typeorm';

@Entity({ name: 'PointOfInterestStatuses', schema: 'lookups' })
export class PointOfInterestStatusEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'Name', length: 20 })
  name: string;

  @Column({ name: 'PrimaryColor', length: 30 })
  primaryColor: string;

  @Column({ name: 'SecondaryColor', length: 30 })
  secondaryColor: string;
}
