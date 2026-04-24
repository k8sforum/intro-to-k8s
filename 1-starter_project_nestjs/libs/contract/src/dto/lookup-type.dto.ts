import { IsString, MaxLength } from 'class-validator';

export class LookupTypeDto {
  id: number;
  name: string;
  @IsString() @MaxLength(30) primaryColor: string;
  @IsString() @MaxLength(30) secondaryColor: string;
}
