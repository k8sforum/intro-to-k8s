import { ApiProperty } from '@nestjs/swagger';

export class SaveEntityResponseDto {
  @ApiProperty({ example: 1 }) id: number;
  @ApiProperty({ example: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890', required: false }) key?: string;
  @ApiProperty({ example: 'Active', required: false }) status?: string;
}
