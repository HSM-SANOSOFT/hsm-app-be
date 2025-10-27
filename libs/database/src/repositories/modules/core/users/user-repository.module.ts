import { UserEntity, UserIntegrationEntity, UserRoleEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';

@Module({
  imports: [
    TypeOrmModule.forFeature([UserEntity, UserRoleEntity, UserIntegrationEntity], Databases.HsmDbPostgres),
  ],
  exports: [TypeOrmModule],
})
export class UserRepositoryModule {}
