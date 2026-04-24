import { Injectable, Inject } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import * as Minio from 'minio';
import { v4 as uuidv4 } from 'uuid';
import * as path from 'path';
import { IObjectStorageService, EntityNotFoundException, RequiredParameterNotFoundException } from '@mytravels/contract';

@Injectable()
export class MinioStorageService implements IObjectStorageService {
  private readonly client: Minio.Client;

  constructor(private readonly config: ConfigService) {
    const endpoint = config.get<string>('MINIO_ENDPOINT');
    const accessKey = config.get<string>('MINIO_ROOT_USER');
    const secretKey = config.get<string>('MINIO_ROOT_PASSWORD');

    const url = new URL(endpoint.startsWith('http') ? endpoint : `http://${endpoint}`);
    this.client = new Minio.Client({
      endPoint: url.hostname,
      port: url.port ? parseInt(url.port) : (url.protocol === 'https:' ? 443 : 9000),
      useSSL: url.protocol === 'https:',
      accessKey,
      secretKey,
    });
  }

  async objectExists(bucketName: string, objectName: string): Promise<boolean> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    const exists = await this.client.bucketExists(bucketName);
    if (!exists) return false;
    try {
      await this.client.statObject(bucketName, objectName);
      return true;
    } catch {
      return false;
    }
  }

  async saveStream(bucketName: string, objectName: string, buffer: Buffer, size: number): Promise<void> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    await this.ensureBucket(bucketName);
    await this.client.putObject(bucketName, objectName, buffer, size, { 'Content-Type': 'application/octet-stream' });
  }

  async saveFile(file: Express.Multer.File, bucketName: string): Promise<string> {
    const ext = path.extname(file.originalname);
    const objectName = `${uuidv4().replace(/-/g, '')}${ext}`;
    await this.saveStream(bucketName, objectName, file.buffer, file.size);
    return objectName;
  }

  async saveBase64(base64: string, bucketName: string, extension: string): Promise<string> {
    const objectName = `${uuidv4().replace(/-/g, '')}${extension}`;
    const buffer = Buffer.from(base64, 'base64');
    await this.saveStream(bucketName, objectName, buffer, buffer.length);
    return objectName;
  }

  async saveObject<T>(obj: T, bucketName: string, objectName: string): Promise<void> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    const json = JSON.stringify(obj);
    const buffer = Buffer.from(json, 'utf8');
    await this.saveStream(bucketName, objectName, buffer, buffer.length);
  }

  async getStream(bucketName: string, objectName: string): Promise<Buffer> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    const exists = await this.client.bucketExists(bucketName);
    if (!exists) throw new EntityNotFoundException(bucketName);
    try {
      const stream = await this.client.getObject(bucketName, objectName);
      return this.streamToBuffer(stream);
    } catch {
      throw new EntityNotFoundException(objectName);
    }
  }

  async getBase64(bucketName: string, objectName: string): Promise<string> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    await this.ensureBucket(bucketName);
    try {
      const stream = await this.client.getObject(bucketName, objectName);
      const buffer = await this.streamToBuffer(stream);
      return buffer.toString('base64');
    } catch {
      return '';
    }
  }

  async getObject<T>(bucketName: string, objectName: string): Promise<T> {
    const buffer = await this.getStream(bucketName, objectName);
    return JSON.parse(buffer.toString('utf8')) as T;
  }

  async listObjects(bucketName: string): Promise<string[]> {
    this.requireParam(bucketName, 'bucketName');
    await this.ensureBucket(bucketName);
    return new Promise((resolve, reject) => {
      const names: string[] = [];
      const stream = this.client.listObjectsV2(bucketName, '', true);
      stream.on('data', (obj) => names.push(obj.name));
      stream.on('error', reject);
      stream.on('end', () => resolve(names));
    });
  }

  async listBuckets(): Promise<string[]> {
    const buckets = await this.client.listBuckets();
    return buckets.map((b) => b.name);
  }

  async removeObject(bucketName: string, objectName: string): Promise<void> {
    this.requireParam(bucketName, 'bucketName');
    this.requireParam(objectName, 'objectName');
    await this.client.removeObject(bucketName, objectName);
  }

  private async ensureBucket(bucketName: string): Promise<void> {
    const exists = await this.client.bucketExists(bucketName);
    if (!exists) await this.client.makeBucket(bucketName);
  }

  private streamToBuffer(stream: NodeJS.ReadableStream): Promise<Buffer> {
    return new Promise((resolve, reject) => {
      const chunks: Buffer[] = [];
      stream.on('data', (chunk) => chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)));
      stream.on('error', reject);
      stream.on('end', () => resolve(Buffer.concat(chunks)));
    });
  }

  private requireParam(value: string, name: string): void {
    if (!value?.trim()) throw new RequiredParameterNotFoundException(name);
  }
}
