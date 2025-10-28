import { Global, Module } from '@nestjs/common';
import { APP_FILTER } from '@nestjs/core';
import { TypeOrmExceptionFilter } from './common/filter/typeorm-exeption.filter';
import { DatabaseRepositoryModule } from './repositories/database-repository.module';
import { DatabaseSourcesModule } from './sources/database-sources.module';

@Global()
@Module({
  providers: [
    {
      provide: APP_FILTER,
      useClass: TypeOrmExceptionFilter,
    },
  ],
  imports: [DatabaseSourcesModule, DatabaseRepositoryModule],
  exports: [DatabaseRepositoryModule, DatabaseSourcesModule],
})
export class DatabaseModule {}
