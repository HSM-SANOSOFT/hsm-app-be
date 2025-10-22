import { Module } from '@nestjs/common';

import { DatabaseModule } from '@hsm-lib/database';
import { QueueModule } from '@hsm-lib/queue';

import { MainController } from './main.controller';
import { MainService } from './main.service';
import { AdministrativeModule } from './modules/administrative/administrative.module';
import { ClinicalModule } from './modules/clinical/clinical.module';
import { CoreModule } from './modules/core/core.module';
import { SecurityModule } from './modules/security/security.module';

@Module({
  imports: [DatabaseModule, QueueModule, CoreModule, SecurityModule, ClinicalModule, AdministrativeModule],
  controllers: [MainController],
  providers: [MainService],
})
export class MainModule {}
