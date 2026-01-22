import {
  UploadDocumentPayloadDto,
  UploadToS3Dto,
} from '@hsm-lib/definitions/dtos';
import { S3Service } from '@hsm-lib/storage/s3/s3.service';
import { Injectable, InternalServerErrorException } from '@nestjs/common';

@Injectable()
export class DocsService {
  constructor(private readonly s3Service: S3Service) {}

  async getDocumentsUrl(
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
    return await this.s3Service.generatePresignedUrls(payload, opts);
  }

  async createDocuments() {
    // Implementation for creating documents
  }
  async deleteDocuments(
    payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
  ) {
    return await this.s3Service.deleteFiles(payload);
  }

  async uploadDocuments(
    payload: UploadDocumentPayloadDto,
    files: Array<Express.Multer.File>,
  ) {
    const fileQueues = new Map<string, Express.Multer.File[]>();
    for (const f of files) {
      const key = (f.originalname ?? '').trim();
      if (!key) continue;

      const fileQueue = fileQueues.get(key) ?? [];
      fileQueue.push(f);
      fileQueues.set(key, fileQueue);
    }

    const data: UploadToS3Dto = {
      payload: payload.payload.map(item => ({
        bucket: item.bucket,
        files: item.files.map(meta => {
          const queue = fileQueues.get(meta.filename);
          const match = queue?.shift();

          if (!match) {
            throw new InternalServerErrorException(
              `No uploaded file matched payload filename="${meta.filename}" (bucket="${item.bucket}")`,
            );
          }

          if (queue && queue.length === 0) {
            fileQueues.delete(meta.filename);
          }

          return {
            filename: meta.filename,
            foldername: meta.foldername,
            data: match.buffer,
            contentType: match.mimetype,
          };
        }),
      })),
    };
    if (fileQueues.size > 0) {
      const extras = Array.from(fileQueues.keys());
      throw new InternalServerErrorException(
        `Uploaded files not referenced in payload: ${extras.join(', ')}`,
      );
    }
    return await this.s3Service.uploadFiles(data);
  }
}
