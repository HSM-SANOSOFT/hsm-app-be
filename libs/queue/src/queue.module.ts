import { BullModule } from '@nestjs/bullmq';
import { Global, Module } from '@nestjs/common';

import { envs } from '@hsm-lib/config';

@Global()
@Module({
  imports: [
    BullModule.forRoot({
      connection: {
        host: envs.HSM_DB_REDIS_HOST,
        port: envs.HSM_DB_REDIS_PORT,
        username: envs.HSM_DB_REDIS_USER,
        password: envs.HSM_DB_REDIS_PASSWORD,
        keyPrefix: 'hsm-app-be-queue:',
      },
      defaultJobOptions: {
        attempts: 3,
        delay: 1000,
        backoff: 2000,
      },
    }),
    BullModule.registerQueue(
      { name: 'email' },
      { name: 'document' },
      { name: 'notification' },
    ),
  ],
  providers: [],
  exports: [BullModule],
})
export class QueueModule {}
