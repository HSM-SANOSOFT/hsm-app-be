import { DefinitionsModule } from '@hsm-lib/definitions';
import { BullModule } from '@nestjs/bullmq';
import { Module } from '@nestjs/common';

import { envs } from './config/envs';
import { MainController } from './main.controller';
import { MainService } from './main.service';
import { DatabaseModule } from './database/database.module';
@Module({
  imports: [
    DefinitionsModule,
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
  controllers: [MainController],
  providers: [MainService],
})
export class MainModule {}
