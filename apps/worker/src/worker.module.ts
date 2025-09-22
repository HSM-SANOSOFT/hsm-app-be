import { BullModule } from '@nestjs/bullmq';
import { Module } from '@nestjs/common';

import { envs } from './config/envs';
import { WorkerController } from './worker.controller';
import { WorkerService } from './worker.service';
import { DatabaseModule } from './database/database.module';

@Module({
  imports: [
    BullModule.forRoot({
      connection: {
        host: envs.HSM_DB_REDIS_HOST,
        port: envs.HSM_DB_REDIS_PORT,
        password: envs.HSM_DB_REDIS_PASSWORD,
        keyPrefix: 'hsm-app-be-queue:',
      },
      defaultJobOptions: {
        attempts: 3,
        delay: 1000,
        backoff: 2000,
      },
    }),
    BullModule.registerQueue({
      name: 'email',
    }),
    DatabaseModule,
  ],
  controllers: [WorkerController],
  providers: [WorkerService],
})
export class WorkerModule {}
