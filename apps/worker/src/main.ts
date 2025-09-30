import { ConsoleLogger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';

import { envs } from '@hsm-lib/config';

import { WorkerModule } from './worker.module';

async function bootstrap() {
  const app = await NestFactory.createApplicationContext(WorkerModule, {
    logger: new ConsoleLogger({
      prefix: 'hsm-app-be-worker',
      json: envs.ENVIRONMENT !== 'dev',
    }),
  });
  app.enableShutdownHooks();
}
void bootstrap();
