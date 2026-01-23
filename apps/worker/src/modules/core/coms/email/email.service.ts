import { Readable } from 'node:stream';
import { envs } from '@hsm-lib/config';
import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import { Inject, Injectable, Logger } from '@nestjs/common';
import nodemailer from 'nodemailer';
import { Attachment } from 'nodemailer/lib/mailer';
import { DocsService } from '../../docs/docs.service';
import { TemplateService } from '../template/template.service';

@Injectable()
export class EmailService {
  private readonly logger = new Logger(EmailService.name);
  constructor(
    @Inject('SMTP_CLIENT') private readonly smtpClient: nodemailer.Transporter,
    private readonly templateService: TemplateService,
    private readonly docsService: DocsService,
  ) {}

  async sendEmail(payload: SendEmailPayloadDto) {
    try {
      const { documents } = payload;
      const attachments: Attachment[] = [];

      if (documents?.length) {
        const documentStream = await this.docsService.getDocumentsStreams({
          documents,
        });

        for (const doc of documentStream) {
          for (const file of doc.files) {
            if (!file.fileStream) {
              this.logger.error(`File stream for ${file.fileId} is undefined`);
              throw new Error(`File stream for ${file.fileId} is undefined`);
            }
            const fileBuffer = Buffer.from(
              await file.fileStream.transformToByteArray(),
            );

            const attachment = {
              filename: file.fileId,
              content: fileBuffer,
              contentType: file.fileContentType,
            };

            this.logger.debug(
              `Attachment added: name: ${attachment.filename}, type: ${attachment.contentType}, content: ${attachment.content.length}`,
            );

            attachments.push(attachment);
          }
        }
      }
      const { subject, html } = this.templateService.getEmailTemplate(
        payload.emailTemplate,
        payload.data,
      );

      const mailOptions: nodemailer.SendMailOptions = {
        from: payload.fromEmail || envs.SMTP_FROM_EMAIL,
        to: payload.toEmails,
        subject: subject,
        html: html,
        attachments: attachments,
      };

      this.logger.log(
        `Sending email to: ${mailOptions.to}, subject: ${mailOptions.subject}, attachments: ${attachments.length}`,
      );

      return await this.smtpClient.sendMail(mailOptions);
    } catch (error) {
      this.logger.error(`Error sending email: ${error}`);
      throw new Error('Email Service', error);
    }
  }
}
