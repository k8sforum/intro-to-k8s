import { ApiProperty } from '@nestjs/swagger';

export class ApiErrorDto {
  @ApiProperty({ example: 'a1b2c3d4e5f6' }) id: string;
  @ApiProperty({ example: 500 }) httpStatusCode: number;
  @ApiProperty({ example: '' }) code: string;
  @ApiProperty({ example: '/api/point-of-interest' }) links: string;
  @ApiProperty({ example: 'An error occurred in the API.' }) title: string;
  @ApiProperty({ example: 'Something went wrong.' }) message: string;
  @ApiProperty({ example: '' }) detail: string;
}
