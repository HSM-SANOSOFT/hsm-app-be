import {
  ApiDocumentation,
  EmailTemplateDataPipe,
  Public,
} from '@hsm-lib/common';
import { SendEmailPayloadDto } from '@hsm-lib/common/definitions/dtos';
import { Body, Controller, Post } from '@nestjs/common';
import { Roles } from '../../security/roles/roles.decorator';
import { ComsService } from './coms.service';

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
