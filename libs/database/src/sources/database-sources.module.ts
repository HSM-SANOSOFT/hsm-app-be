import { Module } from '@nestjs/common';

import { DatabaseOracleModule } from './database-oracle.module';
import { DatabasePostgresModule } from './database-postgres.module';

@Module({
  imports: [DatabaseOracleModule, DatabasePostgresModule],
  controllers: [],
  providers: [],
  exports: [DatabaseOracleModule, DatabasePostgresModule],
})
export class DatabaseSourcesModule {}
