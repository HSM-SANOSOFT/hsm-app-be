import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';

import { Databases } from '@hsm-lib/database/sources';

@Module({
  imports: [TypeOrmModule.forFeature([], Databases.HsmDbPostgres)],
  exports: [TypeOrmModule],
})
export class AuthRepositoryModule {}
