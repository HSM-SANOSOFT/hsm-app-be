import { ApiDocumentation } from '@hsm-lib/common';
import { Role } from '@hsm-lib/definitions/enums';
import { Body, Controller, Delete, Logger, Post } from '@nestjs/common';
import { Roles } from '../../security/roles/roles.decorator';
import { DocsService } from './docs.service';

@Controller('docs')
export class DocsController {
  private readonly logger = new Logger(DocsController.name);
  constructor(private readonly docsService: DocsService) {}
  //TODO: Add endpoint creation, updating, deleting, fetching documents

  @ApiDocumentation()
  @Roles()
  @Post('url')
  async getDocumentsUrl(
    @Body() payload: Array<{
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
    this.logger.debug(
      `Generating document URLs for ${JSON.stringify(payload)}`,
    );
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
  async uploadDocuments(
    @Body() payload: Array<{
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
    // Implementation for uploading a document
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
