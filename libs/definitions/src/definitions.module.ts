import { Module } from '@nestjs/common';
import { DefinitionsService } from './definitions.service';

@Module({
  providers: [DefinitionsService],
  exports: [DefinitionsService],
})
export class DefinitionsModule {}
