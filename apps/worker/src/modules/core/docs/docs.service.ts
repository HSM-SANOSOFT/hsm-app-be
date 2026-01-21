import { S3Service } from '@hsm-lib/storage/s3/s3.service';
import { Injectable } from '@nestjs/common';

@Injectable()
export class DocsService {
  constructor(private readonly s3Service: S3Service) {}
}
