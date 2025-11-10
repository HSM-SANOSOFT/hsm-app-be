import { envs } from '@hsm-lib/config';
import { ConsoleLogger, ValidationPipe, VersioningType } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
import { ResponseFilter } from './common/filters';
import { ResponseInterceptor } from './common/interceptors/response.interceptor';
import { MainModule } from './main.module';

async function bootstrap() {
  const app = await NestFactory.create(MainModule, {
    logger: new ConsoleLogger({
      prefix: 'hsm-app-be-main',
      json: envs.ENVIRONMENT !== 'dev',
      logLevels:
        envs.ENVIRONMENT === 'dev'
          ? ['log', 'error', 'warn', 'debug', 'verbose']
          : ['log', 'error', 'warn'],
    }),
  });
  const config = new DocumentBuilder()
    .setTitle('HSM App Backend')
    .setVersion('1.0')
    .build();

  const docs = () => SwaggerModule.createDocument(app, config);

  SwaggerModule.setup('api', app, docs);

  app.useGlobalGuards();
  app.useGlobalFilters(new ResponseFilter());
  app.useGlobalInterceptors(new ResponseInterceptor());

  app.enableShutdownHooks();
  app.useGlobalPipes(
    new ValidationPipe({
      whitelist: true,
      forbidNonWhitelisted: true,
      transform: true,
      disableErrorMessages: false,
      validationError: { target: false, value: false },
    }),
  );

  app.enableVersioning({
    type: VersioningType.URI,
    defaultVersion: '1',
  });

  await app.listen(3000);
}
void bootstrap();
