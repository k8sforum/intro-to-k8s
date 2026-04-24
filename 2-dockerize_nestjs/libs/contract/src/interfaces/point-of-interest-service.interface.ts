import { GetPointOfInterestResponse } from '../responses/get-point-of-interest.response';

export interface IPointOfInterestService {
  getAll(): Promise<GetPointOfInterestResponse[]>;
  getByTag(tagName: string): Promise<GetPointOfInterestResponse[]>;
  saveFileAsPointOfInterest(file: Express.Multer.File): Promise<number>;
  updateStatus(pointOfInterestKey: string, statusId: number): Promise<number>;
  updatePointOfInterest(file: Express.Multer.File, pointOfInterestKey: string): Promise<number>;
}

export const IPointOfInterestService = Symbol('IPointOfInterestService');
