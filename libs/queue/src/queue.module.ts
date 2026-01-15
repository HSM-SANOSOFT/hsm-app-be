import { envs } from '@hsm-lib/config';
import { BullModule } from '@nestjs/bullmq';
import { Global, Module } from '@nestjs/common';
import { QueueService } from './queue.service';

@Global()
@Module({
  imports: [
    BullModule.forRoot({
      connection: {
        host: envs.HSM_DB_REDIS_HOST,
        port: envs.HSM_DB_REDIS_PORT,
        username: envs.HSM_DB_REDIS_USER,
        password: envs.HSM_DB_REDIS_PASSWORD,
      },
      prefix: 'hsm-app-be-queue',
      defaultJobOptions: {
        attempts: 3,
        delay: 1000,
        backoff: 2000,
      },
    }),
    BullModule.registerQueue(
      { name: 'coms' },
      { name: 'document' },
      { name: 'notification' },
    ),
  ],
  providers: [QueueService],
  exports: [BullModule, QueueService],
})
export class QueueModule {}
