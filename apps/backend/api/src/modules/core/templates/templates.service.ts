import {
  TemplateComEmailEntity,
  TemplateComSmsEntity,
  TemplateDocEntity,
  TemplatesEntity,
} from '@hsm-lib/database/entities/modules/core/template';
import { DatabasesEnum } from '@hsm-lib/database/sources';
import { InjectQueue } from '@nestjs/bullmq';
import { Injectable } from '@nestjs/common';
import { InjectRepository } from '@nestjs/typeorm';
import { Queue } from 'bullmq';
import { Repository } from 'typeorm';

@Injectable()
export class TemplatesService {
  constructor(
    @InjectQueue('templates') private readonly templatesQueue: Queue,
    @InjectRepository(TemplatesEntity, DatabasesEnum.HsmDbPostgres)
    private readonly templatesRepository: Repository<TemplatesEntity>,
    @InjectRepository(TemplateComEmailEntity, DatabasesEnum.HsmDbPostgres)
    private readonly templateComEmailRepository: Repository<TemplateComEmailEntity>,
    @InjectRepository(TemplateComSmsEntity, DatabasesEnum.HsmDbPostgres)
    private readonly templateComSmsRepository: Repository<TemplateComSmsEntity>,
    @InjectRepository(TemplateDocEntity, DatabasesEnum.HsmDbPostgres)
    private readonly templateDocRepository: Repository<TemplateDocEntity>,
  ) {}

  async getTemplate(identifier: string) {
    const template = await this.templatesRepository.findOne({
      where: [{ id: identifier }, { name: identifier }],
    });
    return template;
  }

  async addTemplate(payload: unknown) {
    return 'addTemplate';
  }

  async updateTemplate(payload: unknown) {
    return 'updateTemplate';
  }

  async deleteTemplate(id: string) {
    return 'deleteTemplate';
  }
}
