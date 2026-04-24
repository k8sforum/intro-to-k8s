export class GetPointOfInterestResponse {
  rowId: number;
  pointOfInterestId: number;
  container: string;
  originalFileName: string;
  generatedBlobName: string;
  latitude: number;
  longitude: number;
  dateCreated: Date;
  formattedAddress: string;
  imageResized: boolean;
  tagId: number | null;
  tagName: string;
  tagDateCreated: Date | null;
  pointOfInterestTypeId: number;
  pointOfInterestType: string;
  pointOfInterestKey: string;
  pointOfInterestStatusId: number;
  pointOfInterestStatus: string;
  primaryColor: string;
  secondaryColor: string;
}
