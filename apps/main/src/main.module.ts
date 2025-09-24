import { Module } from '@nestjs/common';

import { DatabaseModule } from '@hsm-lib/database';
import { DefinitionsModule } from '@hsm-lib/definitions';
import { QueueModule } from '@hsm-lib/queue';

import { MainController } from './main.controller';
import { MainService } from './main.service';

@Module({
  imports: [DatabaseModule, DefinitionsModule, QueueModule],
  controllers: [MainController],
  providers: [MainService],
})
export class MainModule {}
