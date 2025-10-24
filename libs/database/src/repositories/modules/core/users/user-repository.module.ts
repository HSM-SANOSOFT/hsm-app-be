import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';

import { UserEntity, UserRoleEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';

@Module({
  imports: [
    TypeOrmModule.forFeature([UserEntity], Databases.HsmDbPostgres),
    TypeOrmModule.forFeature([UserRoleEntity], Databases.HsmDbPostgres),
  ],
  exports: [TypeOrmModule],
})
export class UserRepositoryModule {}
