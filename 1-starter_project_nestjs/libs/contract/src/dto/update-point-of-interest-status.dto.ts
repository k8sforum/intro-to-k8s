import { IsString, IsNotEmpty, MaxLength, IsNumber } from 'class-validator';
import { ApiProperty } from '@nestjs/swagger';

export class UpdatePointOfInterestStatusDto {
  @ApiProperty({ example: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', maxLength: 40 })
  @IsString() @IsNotEmpty() @MaxLength(40) pointOfInterestKey: string;

  @ApiProperty({ example: 2 })
  @IsNumber() @IsNotEmpty() statusId: number;
}
