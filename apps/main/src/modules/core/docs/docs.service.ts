import { S3Service } from '@hsm-lib/storage/s3/s3.service';
import { Injectable } from '@nestjs/common';

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
    payload: Array<{
      bucket: string;
      files: Array<{
        filename: string;
        foldername: string;
        data: Buffer;
        contentType: string;
        cacheControl: string;
      }>;
    }>,
  ) {
    return await this.s3Service.uploadFiles(payload);
  }
}
