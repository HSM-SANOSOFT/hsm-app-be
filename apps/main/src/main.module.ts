import { Module } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';
import { ThrottlerGuard, ThrottlerModule } from '@nestjs/throttler';

import { DatabaseModule } from '@hsm-lib/database';
import { QueueModule } from '@hsm-lib/queue';

import { MainController } from './main.controller';
import { MainService } from './main.service';
import { AdministrativeModule } from './modules/administrative/administrative.module';
import { ClinicalModule } from './modules/clinical/clinical.module';
import { CoreModule } from './modules/core/core.module';
import { SecurityModule } from './modules/security/security.module';

@Module({
  imports: [ThrottlerModule.forRoot(
    {
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
    },
  ), DatabaseModule, QueueModule, CoreModule, SecurityModule, ClinicalModule, AdministrativeModule],
  controllers: [MainController],
  providers: [MainService, {
    provide: APP_GUARD,
    useClass: ThrottlerGuard,
  }],
})
export class MainModule {}
