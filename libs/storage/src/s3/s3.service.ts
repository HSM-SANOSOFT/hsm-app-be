import {
  DeleteObjectCommand,
  GetObjectCommand,
  HeadObjectCommand,
  PutObjectCommand,
  S3Client,
  S3ServiceException,
} from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { UploadToS3Dto } from '@hsm-lib/definitions/dtos';
import { Inject, Injectable, Logger } from '@nestjs/common';
import { randomUUID } from 'crypto';
import { S3_CLIENT, S3_CLIENT_PRESIGNED } from './s3.symbols';

@Injectable()
export class S3Service {
  private readonly logger = new Logger(S3Service.name);
  constructor(
    @Inject(S3_CLIENT) private readonly s3Client: S3Client,
    @Inject(S3_CLIENT_PRESIGNED) private readonly s3ClientPresigned: S3Client,
  ) {}

  private getFileFolder(foldername: string): string {
    return (foldername ?? '')
      .trim()
      .replace(/\\/g, '/')
      .replace(/^\/+|\/+$/g, '')
      .replace(/\/{2,}/g, '/')
      .toLowerCase();
  }
  private getFileKey(foldername: string, fileId: string): string {
    const folder = this.getFileFolder(foldername);
    return folder ? `${folder}/${fileId}` : fileId;
  }

  async uploadFiles(dto: UploadToS3Dto) {
    const results = await Promise.all(
      dto.payload.map(async item => {
        const { bucket, files } = item;

        const uploaded = await Promise.all(
          files.map(async f => {
            const fileId = randomUUID();
            const key = this.getFileKey(f.foldername, fileId);

            try {
              await this.s3Client.send(
                new PutObjectCommand({
                  Bucket: bucket,
                  Key: key,
                  Body: f.data,
                  ContentType: f.contentType,
                  Metadata: {
                    originalFilename: f.filename,
                  },
                }),
              );

              return {
                fileId,
                filename: f.filename,
                key,
              };
            } catch (err: unknown) {
              if (err instanceof S3ServiceException) {
                const status = err.$metadata?.httpStatusCode;
                this.logger.error(
                  `Failed to upload filename="${f.filename}" bucket="${bucket}" key="${key}" status=${status} name=${err.name} fault=${err.$fault} message=${err.message}`,
                );
              } else {
                this.logger.error(
                  `Error uploading filename="${f.filename}" bucket="${bucket}" key="${key}": ${
                    err instanceof Error ? err.message : String(err)
                  }`,
                );
              }
              throw err;
            }
          }),
        );

        return { bucket, files: uploaded };
      }),
    );

    return results;
  }

  async checkFilesExist(
    payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
  ) {
    const result = await Promise.all(
      payload.map(async item => {
        const { bucket, files } = item;
        const existence = await Promise.all(
          files.map(async file => {
            const key = this.getFileKey(file.foldername, file.fileId);

            try {
              await this.s3Client.send(
                new HeadObjectCommand({
                  Bucket: bucket,
                  Key: key,
                }),
              );
              return { fileId: file.fileId, key, exists: true };
            } catch (err: unknown) {
              if (err instanceof S3ServiceException) {
                const status = err.$metadata?.httpStatusCode;
                if (status === 404) {
                  this.logger.warn(
                    `File does not exist in bucket "${bucket}": key="${key}"-> name: ${err.name} - message: ${err.message} - status: ${status} - fault: ${err.$fault}`,
                  );
                  return { fileId: file.fileId, key, exists: false };
                }
              } else {
                this.logger.error(
                  `Error checking existence of file (key="${key}") in bucket "${bucket}": ${
                    err instanceof Error ? err.message : String(err)
                  }`,
                );
              }
              throw err;
            }
          }),
        );
        return {
          bucket,
          files: existence,
        };
      }),
    );
    return result;
  }

  async deleteFiles(
    payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
  ) {
    const results = await Promise.all(
      payload.map(async item => {
        const { bucket, files } = item;
        const deletions = await Promise.all(
          files.map(async file => {
            const key = this.getFileKey(file.foldername, file.fileId);
            try {
              await this.s3Client.send(
                new DeleteObjectCommand({
                  Bucket: bucket,
                  Key: key,
                }),
              );
            } catch (err: unknown) {
              if (err instanceof S3ServiceException) {
                this.logger.error(
                  `Error deleting file (key="${key}") from bucket "${bucket}": name: ${err.name} - message: ${err.message} - fault: ${err.$fault}`,
                );
              } else {
                this.logger.error(
                  `Error deleting file (key="${key}") from bucket "${bucket}": ${
                    err instanceof Error ? err.message : String(err)
                  }`,
                );
              }
            }
            return {
              fileId: file.fileId,
              key: key,
            };
          }),
        );
        return {
          bucket,
          files: deletions,
        };
      }),
    );
    return results;
  }

  async getFiles(
    payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
  ) {
    const results = await Promise.all(
      payload.map(async item => {
        const { bucket, files } = item;
        const contents = await Promise.all(
          files.map(async file => {
            const key = this.getFileKey(file.foldername, file.fileId);
            try {
              const content = await this.s3Client.send(
                new GetObjectCommand({ Bucket: bucket, Key: key }),
              );
              return { fileId: file.fileId, key, content: content };
            } catch (err: unknown) {
              if (err instanceof S3ServiceException) {
                if (err.$metadata?.httpStatusCode === 404) {
                  this.logger.warn(
                    `File not found in bucket "${bucket}": key="${key}"-> name: ${err.name} - message: ${err.message} - status: ${err.$metadata?.httpStatusCode} - fault: ${err.$fault}`,
                  );
                  return { fileId: file.fileId, key, content: null };
                }
                if (err.$metadata?.httpStatusCode === 403) {
                  this.logger.error(
                    `Access denied to file in bucket "${bucket}": key="${key}"-> name: ${err.name} - message: ${err.message} - status: ${err.$metadata?.httpStatusCode} - fault: ${err.$fault}`,
                  );
                } else {
                  this.logger.error(
                    `Error getting file (key="${key}") from bucket "${bucket}": name: ${err.name} - message: ${err.message} - fault: ${err.$fault}`,
                  );
                }
              } else {
                this.logger.error(
                  `Error getting file (key="${key}") from bucket "${bucket}": ${
                    err instanceof Error ? err.message : String(err)
                  }`,
                );
              }
              throw err;
            }
          }),
        );
        return {
          bucket,
          files: contents,
        };
      }),
    );
    return results;
  }

  async generatePresignedUrls(
    payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
    opts?: {
      expiresInSeconds?: number;
      download?: boolean;
    },
  ) {
    const results = await Promise.all(
      payload.map(async item => {
        const { bucket, files } = item;

        const urls = await Promise.all(
          files.map(async file => {
            const key = this.getFileKey(file.foldername, file.fileId);
            const contentDisposition = opts?.download ? 'attachment' : 'inline';

            try {
              const cmd = new GetObjectCommand({
                Bucket: bucket,
                Key: key,
                ResponseContentDisposition: contentDisposition,
              });

              const url = await getSignedUrl(this.s3ClientPresigned, cmd, {
                expiresIn: opts?.expiresInSeconds,
              });

              return { fileId: file.fileId, key, url };
            } catch (err: unknown) {
              if (err instanceof S3ServiceException) {
                const status = err.$metadata?.httpStatusCode;
                this.logger.error(
                  `Error generating presigned URL for bucket="${bucket}" key="${key}": ${err.name} (${status}) ${err.message}`,
                );
              } else {
                this.logger.error(
                  `Error generating presigned URL for bucket="${bucket}" key="${key}": ${
                    err instanceof Error ? err.message : String(err)
                  }`,
                );
              }
              throw err;
            }
          }),
        );

        return { bucket, files: urls };
      }),
    );

    return results;
  }
}
