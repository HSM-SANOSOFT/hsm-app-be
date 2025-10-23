import { Global, Module } from '@nestjs/common';

import { DatabaseRepositories } from './repositories/database.repositories';
import { DatabaseSources } from './sources/database.sources';

@Global()
@Module({
  imports: [...DatabaseSources, ...DatabaseRepositories],
  exports: [...DatabaseSources, ...DatabaseRepositories],
})
export class DatabaseModule {}
