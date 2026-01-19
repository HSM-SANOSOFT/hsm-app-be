import { Module } from '@nestjs/common';
import { S3Module } from './s3/s3.module';
import { StorageService } from './storage.service';

@Module({
  providers: [StorageService],
  exports: [StorageService],
  imports: [S3Module],
})
export class StorageModule {}
