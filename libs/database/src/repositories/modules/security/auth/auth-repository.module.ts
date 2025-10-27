import { RefreshTokenUserEntity, RefreshTokenUserIntegrationEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';

@Module({
  imports: [TypeOrmModule.forFeature([RefreshTokenUserEntity, RefreshTokenUserIntegrationEntity], Databases.HsmDbPostgres)],
  exports: [TypeOrmModule],
})
export class AuthRepositoryModule {}
