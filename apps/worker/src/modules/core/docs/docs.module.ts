import { StorageModule } from '@hsm-lib/storage';
import { Module } from '@nestjs/common';
import { DocsService } from './docs.service';
import { GenerationModule } from './generation/generation.module';

@Module({
  imports: [GenerationModule, StorageModule],
  providers: [DocsService],
})
export class DocsModule {}
