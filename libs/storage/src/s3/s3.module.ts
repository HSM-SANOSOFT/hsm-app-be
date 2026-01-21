// s3.module.ts
import { S3Client, S3ServiceException } from '@aws-sdk/client-s3';
import { envs } from '@hsm-lib/config';
import { Logger, Module } from '@nestjs/common';
import { S3Initializer } from './s3.initializer';
import { S3Service } from './s3.service';
import { S3_CLIENT, S3_INIT } from './s3.symbols';

@Module({
  providers: [
    S3Service,
    S3Initializer,
    {
      provide: S3_CLIENT,
      useFactory: () => {
        const logger = new Logger('S3Module');
        try {
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
        } catch (err: unknown) {
          if (err instanceof S3ServiceException) {
            logger.error(
              `Failed to create S3 client: ${err.name} - ${err.message} - ${err.$fault}`,
            );
          } else {
            logger.error(
              `Failed to create S3 client: ${
                err instanceof Error ? err.message : String(err)
              }`,
            );
          }
          throw err;
        }
      },
    },
    {
      provide: S3_INIT,
      inject: [S3Initializer],
      useFactory: async (init: S3Initializer) => {
        await init.ensureBuckets();
        return true;
      },
    },
  ],
  exports: [S3Service, S3_CLIENT],
})
export class S3Module {}
