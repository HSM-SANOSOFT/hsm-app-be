import { SendEmailPayloadDto } from '@hsm-lib/definitions/dtos';
import { Processor, WorkerHost } from '@nestjs/bullmq';
import { Job } from 'bullmq';
import { EmailService } from './email/email.service';

@Processor('coms')
export class ComsService extends WorkerHost {
  constructor(private readonly emailService: EmailService) {
    super();
  }
  async process(job: Job<unknown, unknown, string>): Promise<unknown> {
    switch (job.name) {
      case 'send-email': {
        const payload = job.data as SendEmailPayloadDto;
        return await this.emailService.sendEmail(payload);
      }
      default:
        throw new Error(`Unknown job name: ${job.name}`);
    }
  }
}
