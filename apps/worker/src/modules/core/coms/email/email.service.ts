import { envs } from '@hsm-lib/config';
import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import {
  Inject,
  Injectable,
  InternalServerErrorException,
} from '@nestjs/common';
import nodemailer from 'nodemailer';
import { Attachment } from 'nodemailer/lib/mailer';
import { TemplateService } from '../template/template.service';

@Injectable()
export class EmailService {
  constructor(
    @Inject('SMTP_CLIENT') private readonly smtpClient: nodemailer.Transporter,
    private readonly templateService: TemplateService,
  ) {}

  async sendEmail(payload: SendEmailPayloadDto) {
    try {
      const { subject, html } = this.templateService.getEmailTemplate(
        payload.emailTemplate,
        payload.data,
      );

      const attachments: Attachment[] =
        payload.files?.map((file: Express.Multer.File) => ({
          filename: file.originalname,
          content: file.buffer,
        })) ?? [];

      const mailOptions: nodemailer.SendMailOptions = {
        from: payload.fromEmail || envs.SMTP_FROM_EMAIL,
        to: payload.toEmails,
        subject: subject,
        html: html,
        attachments: attachments,
      };

      return await this.smtpClient.sendMail(mailOptions);
    } catch (error) {
      throw new InternalServerErrorException('Email Service', error);
    }
  }
}
