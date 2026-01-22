import { HttpLoggingInterceptor } from '@hsm-lib/common';
import { envs } from '@hsm-lib/config';
import { ConsoleLogger, ValidationPipe, VersioningType } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { DocumentBuilder, SwaggerModule } from '@nestjs/swagger';
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
    .addBearerAuth(undefined, 'access_token')
    .addBearerAuth(undefined, 'refresh_token')
    .build();

  const docs = () => SwaggerModule.createDocument(app, config);

  SwaggerModule.setup('api', app, docs, {
    customSiteTitle: envs.SWAGGER_SITE_TITLE,
    customfavIcon: envs.SWAGGER_FAVICON,
  });

  app.useGlobalGuards();
  app.useGlobalFilters();
  app.useGlobalInterceptors(new HttpLoggingInterceptor());

  app.enableShutdownHooks();
  app.useGlobalPipes(
    new ValidationPipe({
      transform: true,
      whitelist: true,
      forbidNonWhitelisted: true,
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
