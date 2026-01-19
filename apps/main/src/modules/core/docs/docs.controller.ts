import { ApiDocumentation } from '@hsm-lib/common';
import { Role } from '@hsm-lib/definitions/enums';
import { Controller, Get, Post, Patch, Delete, Param } from '@nestjs/common';
import { Roles } from '../../security/roles/roles.decorator';

@Controller('docs')
export class DocsController {
  //TODO: Add endpoint creation, updating, deleting, fetching documents

  @ApiDocumentation()
  @Roles()
  @Get(':id/url')
  async getDocumentsUrl(@Param('id') id: string) {
    // Implementation for retrieving documents
  }

  @ApiDocumentation()
  @Roles()
  @Get(':id/content')
  async getDocumentsContent(@Param('id') id: string) {
    // Implementation for retrieving documents
  }

  @ApiDocumentation()
  @Roles()
  @Post()
  async createDocument() {
    // Implementation for creating a document
  }

  @ApiDocumentation()
  @Roles()
  @Patch(':id')
  async updateDocument(@Param('id') id: string) {
    // Implementation for updating a document
  }

  @ApiDocumentation()
  @Roles()
  @Delete(':id')
  async deleteDocument(@Param('id') id: string) {
    // Implementation for deleting a document
  }
}
