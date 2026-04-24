import { MaxLength } from 'class-validator';
import { ApiProperty } from '@nestjs/swagger';

export class TagDto {
  @ApiProperty({ example: 1 }) id: number;
  @ApiProperty({ example: 'beach', maxLength: 30 }) @MaxLength(30) name: string;
  @ApiProperty({ example: '2024-01-15T00:00:00.000Z' }) dateCreated: Date = new Date();
}
