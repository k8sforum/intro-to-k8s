import { IsString, IsNotEmpty, MaxLength, IsNumber, Min, Max } from 'class-validator';

export class UpdateAddressDto {
  @IsString() @IsNotEmpty() @MaxLength(40) pointOfInterestKey: string;
  @IsNumber() @Min(-90) @Max(90) @IsNotEmpty() latitude: number;
  @IsNumber() @Min(-180) @Max(180) @IsNotEmpty() longitude: number;
  formattedAddress: string;
}
