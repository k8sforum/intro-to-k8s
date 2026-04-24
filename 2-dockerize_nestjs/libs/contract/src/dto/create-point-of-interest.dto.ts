export class CreatePointOfInterestDto {
  originalFileName: string;
  blobName: string;
  latitude: number;
  longitude: number;
  pointOfInterestTypeId: number;
  formattedAddress: string;
}
