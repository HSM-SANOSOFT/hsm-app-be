import { envs } from '@hsm-lib/config';
import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import {
  Inject,
  Injectable,
  InternalServerErrorException,
  Logger,
} from '@nestjs/common';
import nodemailer from 'nodemailer';
import { Attachment } from 'nodemailer/lib/mailer';
import { TemplateService } from '../template/template.service';

@Injectable()
export class EmailService {
  private readonly logger = new Logger(EmailService.name);
  constructor(
    @Inject('SMTP_CLIENT') private readonly smtpClient: nodemailer.Transporter,
    private readonly templateService: TemplateService,
  ) {}

  async sendEmail(payload: SendEmailPayloadDto) {
    const dataToLog = {
      ...payload,
      files: payload.files?.map((file: Express.Multer.File) => ({
        filename: file.originalname,
        mimetype: file.mimetype,
        encoding: file.encoding,
        buffer: 'Buffer [...] for logging purposes',
      })),
    };
    this.logger.debug(
      `Sending email with payload: ${JSON.stringify(dataToLog)}`,
    );
    try {
      const { subject, html } = this.templateService.getEmailTemplate(
        payload.emailTemplate,
        payload.data,
      );

      type JsonBuffer = { type: 'Buffer'; data: number[] };

      function isJsonBuffer(value: unknown): value is JsonBuffer {
        if (value == null || typeof value !== 'object') return false;

        const v = value as Record<string, unknown>;
        return (
          v.type === 'Buffer' &&
          Array.isArray(v.data) &&
          v.data.every(n => typeof n === 'number')
        );
      }

      function toRealBuffer(b: unknown): Buffer {
        if (Buffer.isBuffer(b)) return b;
        if (isJsonBuffer(b)) return Buffer.from(b.data);
        throw new TypeError('Invalid buffer payload');
      }

      const attachments: Attachment[] =
        payload.files?.map((file: Express.Multer.File) => ({
          filename: file.originalname,
          content: toRealBuffer(file.buffer),
          contentType: file.mimetype,
          encoding: file.encoding,
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
      this.logger.error(`Error sending email: ${error}`);
      throw new error('Email Service', error);
    }
  }
}
