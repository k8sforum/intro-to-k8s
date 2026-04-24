export interface IObjectStorageService {
  getBase64(bucketName: string, objectName: string): Promise<string>;
  getObject<T>(bucketName: string, objectName: string): Promise<T>;
  getStream(bucketName: string, objectName: string): Promise<Buffer>;
  listObjects(bucketName: string): Promise<string[]>;
  listBuckets(): Promise<string[]>;
  objectExists(bucketName: string, objectName: string): Promise<boolean>;
  removeObject(bucketName: string, objectName: string): Promise<void>;
  saveFile(file: Express.Multer.File, bucketName: string): Promise<string>;
  saveStream(bucketName: string, objectName: string, stream: Buffer, size: number): Promise<void>;
  saveObject<T>(obj: T, bucketName: string, objectName: string): Promise<void>;
  saveBase64(base64: string, bucketName: string, extension: string): Promise<string>;
}

export const IObjectStorageService = Symbol('IObjectStorageService');
