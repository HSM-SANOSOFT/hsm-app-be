import { ApiDocumentation, Public } from '@hsm-app/server/decorator';
import { ComsService } from '@hsm-app/server/modules/core/coms/coms.service';
import { Roles } from '@hsm-app/server/modules/security/roles/roles.decorator';
import { EmailTemplateDataPipe } from '@hsm-app/server/pipes';
import { SendEmailPayloadDto } from
'@hsm-lib/common/

import { Body, Controller, Post } from '@nestjs/common';

@Controller('coms')
export class ComsController {
  constructor(private readonly comsService: ComsService) {}
  //TODO: Add endpoint implementations

  @ApiDocumentation()
  @Public()
  @Post('send/email')
  async sendEmail(@Body(EmailTemplateDataPipe) payload: SendEmailPayloadDto) {
    await this.comsService.sendEmail(payload);
  }

  @ApiDocumentation()
  @Roles()
  @Post('resend/email')
  async resendEmail() {
    await this.comsService.resendEmail();
  }

  @ApiDocumentation()
  @Roles()
  @Post('send/sms')
  async sendSms() {
    await this.comsService.sendSms();
  }
}
