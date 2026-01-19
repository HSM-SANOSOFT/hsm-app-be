import { StorageModule } from '@hsm-lib/storage';
import { Module } from '@nestjs/common';
import { GenerationModule } from './generation/generation.module';

@Module({
  imports: [GenerationModule, StorageModule],
})
export class DocsModule {}
