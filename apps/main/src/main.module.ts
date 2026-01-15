import { EmailTemplateDataValidator } from '@hsm-lib/common';
import { ResponseFilter } from '@hsm-lib/common/filters';
import { ResponseInterceptor } from '@hsm-lib/common/interceptors';
import { DatabaseModule } from '@hsm-lib/database';
import { QueueModule } from '@hsm-lib/queue';
import { Module } from '@nestjs/common';
import { APP_FILTER, APP_GUARD, APP_INTERCEPTOR } from '@nestjs/core';
import { TerminusModule } from '@nestjs/terminus';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';
import { MainController } from './main.controller';
import { MainService } from './main.service';
import { AdministrativeModule } from './modules/administrative/administrative.module';
import { ClinicalModule } from './modules/clinical/clinical.module';
import { CoreModule } from './modules/core/core.module';
import { SecurityModule } from './modules/security/security.module';

@Module({
  imports: [
    ThrottlerModule.forRoot({
      throttlers: [
        {
          name: 'short',
          ttl: 1000,
          limit: 3,
        },
        {
          name: 'medium',
          ttl: 10000,
          limit: 20,
        },
        {
          name: 'long',
          ttl: 60000,
          limit: 100,
        },
      ],
    }),
    DatabaseModule,
    QueueModule,
    CoreModule,
    SecurityModule,
    ClinicalModule,
    AdministrativeModule,
    TerminusModule,
  ],
  controllers: [MainController],
  providers: [
    MainService,
    {
      provide: APP_GUARD,
      useClass: ThrottlerGuard,
    },
    {
      provide: APP_FILTER,
      useClass: ResponseFilter,
    },
    {
      provide: APP_INTERCEPTOR,
      useClass: ResponseInterceptor,
    },
    EmailTemplateDataValidator,
  ],
  exports: [EmailTemplateDataValidator],
})
export class MainModule {}
