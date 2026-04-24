import { Module } from '@nestjs/common';
import { IObjectStorageService } from '@mytravels/contract';
import { MinioStorageService } from './minio-storage.service';

@Module({
  providers: [
    { provide: IObjectStorageService, useClass: MinioStorageService },
  ],
  exports: [IObjectStorageService],
})
export class StorageModule {}
