import { S3Client } from '@aws-sdk/client-s3';
import { envs } from '@hsm-lib/config';
import { Module } from '@nestjs/common';
import { S3Service } from './s3.service';

export const S3_CLIENT = Symbol('S3_CLIENT');

@Module({
  providers: [
    S3Service,
    {
      provide: S3_CLIENT,
      useFactory: () => {
        return new S3Client({
          region: envs.HSM_STORAGE_S3_REGION,
          endpoint: envs.HSM_STORAGE_S3_FORCE_PATH_STYLE
            ? envs.HSM_STORAGE_S3_HOST
            : undefined,
          forcePathStyle: envs.HSM_STORAGE_S3_FORCE_PATH_STYLE,
          credentials: {
            accessKeyId: envs.HSM_STORAGE_S3_ACCESS_KEY,
            secretAccessKey: envs.HSM_STORAGE_S3_SECRET_KEY,
          },
        });
      },
    },
  ],
  exports: [S3Service, S3_CLIENT],
})
export class S3Module {}
