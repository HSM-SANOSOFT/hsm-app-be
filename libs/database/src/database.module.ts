import { Global, Module } from '@nestjs/common';

import { DatabaseRepositoryModule } from './repositories/database-repository.module';
import { DatabaseSourcesModule } from './sources/database-sources.module';

@Global()
@Module({
  imports: [DatabaseSourcesModule, DatabaseRepositoryModule],
  exports: [DatabaseRepositoryModule, DatabaseSourcesModule],
})
export class DatabaseModule {}
