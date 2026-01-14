import { Module } from '@nestjs/common';

import { EmailModule } from './email/email.module';
import { SmsModule } from './sms/sms.module';
import { TemplateModule } from './template/template.module';
import { ComsService } from './coms.service';

@Module({
  imports: [EmailModule, SmsModule, TemplateModule],
  providers: [ComsService],
})
export class ComsModule {}
