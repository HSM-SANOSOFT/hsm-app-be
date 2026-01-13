import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import { Injectable } from '@nestjs/common';

@Injectable()
export class ComsService {
  async sendEmail(payload: SendEmailPayloadDto) {
    // Implementation for sending email
  }

  async resendEmail() {
    // Implementation for resending email
  }

  async sendSms() {
    // Implementation for sending SMS
  }
}
