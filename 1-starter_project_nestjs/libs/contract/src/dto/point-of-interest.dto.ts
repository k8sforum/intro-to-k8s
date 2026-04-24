import { MaxLength } from 'class-validator';
import { ApiProperty } from '@nestjs/swagger';
import { TagDto } from './tag.dto';

export class PointOfInterestDto {
  @ApiProperty({ example: 1 }) id: number;
  @ApiProperty({ example: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890' }) pointOfInterestKey: string;
  @ApiProperty({ example: -33.9249 }) latitude: number;
  @ApiProperty({ example: 18.4241 }) longitude: number;
  @ApiProperty({ example: 1 }) pointOfInterestTypeId: number;
  @ApiProperty({ example: 'Restaurant' }) pointOfInterestType: string;
  @ApiProperty({ example: 1 }) pointOfInterestStatusId: number;
  @ApiProperty({ example: 'Active' }) pointOfInterestStatus: string;
  @ApiProperty({ example: '#FF5733' }) primaryColor: string;
  @ApiProperty({ example: '#FFFFFF' }) secondaryColor: string;
  @ApiProperty({ example: '2024-01-15T00:00:00.000Z' }) dateCreated: Date = new Date();
  @ApiProperty({ example: '123 Main St, Cape Town, South Africa', maxLength: 300 }) @MaxLength(300) formattedAddress: string;
  @ApiProperty({ type: [TagDto] }) tags: TagDto[] = [];
}
