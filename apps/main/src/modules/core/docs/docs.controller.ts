import { ApiDocumentation } from '@hsm-lib/common';
import { Role } from '@hsm-lib/definitions/enums';
import { Controller, Get, Post } from '@nestjs/common';
import { Roles } from '../../security/roles/roles.decorator';

@Controller('docs')
export class DocsController {
  //TODO: Add endpoint creation, updating, deleting, fetching documents

  @ApiDocumentation()
  @Roles()
  @Get(':id')
  async getDocuments() {
    // Implementation for retrieving documents
  }

  @ApiDocumentation()
  @Roles()
  @Post('create')
  async createDocument() {
    // Implementation for creating a document
  }

  @ApiDocumentation()
  @Roles()
  @Post('update')
  async updateDocument() {
    // Implementation for updating a document
  }

  @ApiDocumentation()
  @Roles()
  @Post('delete')
  async deleteDocument() {
    // Implementation for deleting a document
  }
}
