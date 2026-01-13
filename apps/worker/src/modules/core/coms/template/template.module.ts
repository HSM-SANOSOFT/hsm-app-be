import { Module } from '@nestjs/common';
import { TemplateService } from './template.service';

@Module({
  providers: [TemplateService]
})
export class TemplateModule {}
