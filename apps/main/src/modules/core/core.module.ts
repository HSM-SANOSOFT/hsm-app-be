import { Module } from '@nestjs/common';

import { ComsModule } from './coms/coms.module';
import { DocsModule } from './docs/docs.module';

@Module({
  imports: [DocsModule, ComsModule],
})
export class CoreModule {}
