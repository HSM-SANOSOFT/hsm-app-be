import { Module } from '@nestjs/common';

import { EmailModule } from './email/email.module';
import { SmsModule } from './sms/sms.module';
import { TemplateModule } from './template/template.module';

@Module({
  imports: [EmailModule, SmsModule, TemplateModule],
})
export class ComsModule {}
