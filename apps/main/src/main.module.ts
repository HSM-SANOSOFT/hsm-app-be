import { Module } from '@nestjs/common';
import { DefinitionsModule } from 'hsm-lib/definitions';

import { MainController } from './main.controller';
import { MainService } from './main.service';
@Module({
  imports: [DefinitionsModule],
  controllers: [MainController],
  providers: [MainService],
})
export class MainModule {}
