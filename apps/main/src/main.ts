import { ConsoleLogger } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';

import { MainModule } from './main.module';

async function bootstrap() {
  const app = await NestFactory.create(MainModule, {
    logger: new ConsoleLogger({
      prefix: 'hsm-app-be-main',
      json: true,
    }),
  });
  app.enableShutdownHooks();
  await app.listen(3000);
}
void bootstrap();
