import { IsNumber, IsNotEmpty, Min, Max } from 'class-validator';

export class SaveCoordinatesDto {
  @IsNumber() @Min(-90) @Max(90) @IsNotEmpty() latitude: number;
  @IsNumber() @Min(-180) @Max(180) @IsNotEmpty() longitude: number;
  @IsNumber() @Min(1) @Max(3) @IsNotEmpty() pointOfInterestTypeId: number;
  formattedAddress: string;
}
