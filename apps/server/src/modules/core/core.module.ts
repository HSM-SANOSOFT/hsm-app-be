import { Module } from '@nestjs/common';

import { ComsModule } from './coms/coms.module';
import { DocsModule } from './docs/docs.module';
import { UsersModule } from './users/users.module';

@Module({
  imports: [DocsModule, ComsModule, UsersModule],
})
export class CoreModule {}
