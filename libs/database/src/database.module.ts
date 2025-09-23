import { Global, Module } from '@nestjs/common';

import { DatabaseSources } from './sources/database.sources';

@Global()
@Module({
  imports: [...DatabaseSources],
  exports: [...DatabaseSources],
})
export class DatabaseModule {}
