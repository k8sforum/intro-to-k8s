import { Entity, PrimaryGeneratedColumn, Column } from 'typeorm';

@Entity({ name: 'PointOfInterestTypes', schema: 'lookups' })
export class PointOfInterestTypeEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'Name', length: 20 })
  name: string;

  @Column({ name: 'PrimaryColor', length: 30 })
  primaryColor: string;

  @Column({ name: 'SecondaryColor', length: 30 })
  secondaryColor: string;
}
