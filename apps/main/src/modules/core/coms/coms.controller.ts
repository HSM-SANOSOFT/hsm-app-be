import { ApiDocumentation, Public } from '@hsm-lib/common';
import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import { Role } from '@hsm-lib/definitions/enums';
import {
  BadRequestException,
  Body,
  Controller,
  Post,
  UploadedFiles,
  UseInterceptors,
} from '@nestjs/common';
import { FilesInterceptor } from '@nestjs/platform-express';
import { plainToInstance } from 'class-transformer';
import { validateOrReject } from 'class-validator';
import { Roles } from '../../security/roles/roles.decorator';
import { ComsService } from './coms.service';

@Controller('coms')
export class ComsController {
  constructor(private readonly comsService: ComsService) {}
  //TODO: Add endpoint implementations

  @ApiDocumentation()
  @Public()
  @Post('send/email')
  @UseInterceptors(FilesInterceptor('files'))
  async sendEmail(
    @Body() payload: any,
    @UploadedFiles() files?: Array<Express.Multer.File>,
  ) {
    const parsed: SendEmailPayloadDto = {
      ...payload,
      toEmails:
        typeof payload.toEmails === 'string'
          ? JSON.parse(payload.toEmails)
          : payload.toEmails,
      data:
        typeof payload.data === 'string'
          ? JSON.parse(payload.data)
          : payload.data,
      files: files || [],
    };

    const dto = plainToInstance(SendEmailPayloadDto, parsed);
    await validateOrReject(dto).catch(e => {
      throw new BadRequestException(e);
    });
    await this.comsService.sendEmail(dto);
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
