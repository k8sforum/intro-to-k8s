import { Entity, PrimaryGeneratedColumn, Column, Index, CreateDateColumn } from 'typeorm';

@Entity('Tags')
export class TagEntity {
  @PrimaryGeneratedColumn({ name: 'Id' })
  id: number;

  @Column({ name: 'Name', length: 30 })
  @Index({ unique: true })
  name: string;

  @CreateDateColumn({ name: 'DateCreated' })
  dateCreated: Date;
}
