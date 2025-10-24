import { Module } from '@nestjs/common';
import { GenerationModule } from './generation/generation.module';

@Module({
  imports: [GenerationModule],
})
export class DocsModule {}
