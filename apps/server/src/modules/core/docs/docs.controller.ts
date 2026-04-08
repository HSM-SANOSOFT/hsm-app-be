import { ApiDocumentation } from '@hsm-app/server/decorator';
import { DocsService } from '@hsm-app/server/modules/core/docs/docs.service';
import { Roles } from '@hsm-app/server/modules/security/roles/roles.decorator';
import {
  DocumentsPayloadDto,
  UploadDocumentPayloadDto,
} from '@hsm-lib/common/dtos';

import {
  Body,
  Controller,
  Delete,
  Logger,
  Post,
  Query,
  UploadedFiles,
  UseInterceptors,
} from '@nestjs/common';
import { FilesInterceptor } from '@nestjs/platform-express';

@Controller('docs')
export class DocsController {
  private readonly logger = new Logger(DocsController.name);
  constructor(private readonly docsService: DocsService) {}
  //TODO: Add endpoint creation, updating, deleting, fetching documents

  @ApiDocumentation()
  @Roles()
  @Post('url')
  async getDocumentsUrl(
    @Body() payload: DocumentsPayloadDto,
    @Query('contentDisposition') contentDisposition?: string,
    @Query('expiresInSeconds') expiresInSeconds?: number,
  ) {
    this.logger.debug(
      `Generating document URLs for ${JSON.stringify(payload)}`,
    );
    const opts = {
      contentDisposition,
      expiresInSeconds: expiresInSeconds,
    };
    return await this.docsService.getDocumentsUrl(payload, opts);
  }

  @ApiDocumentation()
  @Roles()
  @Post('create')
  async createDocuments() {
    // Implementation for creating a document
  }

  @ApiDocumentation()
  @Roles()
  @Post('upload')
  @UseInterceptors(FilesInterceptor('files'))
  async uploadDocuments(
    @Body() body: UploadDocumentPayloadDto,
    @UploadedFiles() files: Array<Express.Multer.File>,
  ) {
    const toLog = {
      payload: body.payload.map(p => p),
      files: files.map(f => ({ name: f.originalname, type: f.mimetype })),
    };
    this.logger.debug(`Uploading documents ${JSON.stringify(toLog)}`);
    return await this.docsService.uploadDocuments(body, files);
  }

  @ApiDocumentation()
  @Roles()
  @Delete()
  async deleteDocuments(
    @Body() payload: Array<{
      bucket: string;
      files: Array<{
        foldername: string;
        fileId: string;
      }>;
    }>,
  ) {
    // Implementation for deleting a document
  }
}
