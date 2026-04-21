import {
  Body,
  Controller,
  Delete,
  Get,
  Param,
  Post,
  Put,
} from '@nestjs/common';

import { ApiDocumentation, Public } from '../../../decorator';

import { TemplatesService } from './templates.service';

@Controller('templates')
export class TemplatesController {
  constructor(private readonly templatesService: TemplatesService) {}

  @ApiDocumentation()
  @Public()
  @Get(':identifier')
  async getTemplate(@Param('identifier') identifier: string) {
    await this.templatesService.getTemplate(identifier);
  }

  @ApiDocumentation()
  @Public()
  @Post()
  async addTemplate(@Body('payload') payload: unknown) {
    await this.templatesService.addTemplate(payload);
  }

  @ApiDocumentation()
  @Public()
  @Put()
  async updateTemplate(@Body('payload') payload: unknown) {
    await this.templatesService.updateTemplate(payload);
  }

  @ApiDocumentation()
  @Public()
  @Delete(':id')
  async deleteTemplate(@Param('id') id: string) {
    await this.templatesService.deleteTemplate(id);
  }
}
