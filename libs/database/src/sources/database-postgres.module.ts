import { envs } from '@hsm-lib/config';
import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';

import {
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
  UserEntity,
  UserIntegrationEntity,
  UserRoleEntity,
} from '../entities/modules';
import { Databases } from './database-source.enum';
import { DatabaseSourceOptions } from './database-source-options';

const entities = [
  UserEntity,
  UserRoleEntity,
  UserIntegrationEntity,
  RefreshTokenUserEntity,
  RefreshTokenUserIntegrationEntity,
];

@Module({
  imports: [
    TypeOrmModule.forRoot({
      type: 'postgres',
      name: Databases.HsmDbPostgres,
      host: envs.HSM_DB_POSTGRES_HOST,
      port: envs.HSM_DB_POSTGRES_PORT,
      username: envs.HSM_DB_POSTGRES_USER,
      password: envs.HSM_DB_POSTGRES_PASSWORD,
      database: envs.HSM_DB_POSTGRES_DB,
      synchronize: envs.ENVIRONMENT === 'dev',
      entities,
      ...DatabaseSourceOptions,
    }),
    TypeOrmModule.forFeature(entities, Databases.HsmDbPostgres),
  ],
  controllers: [],
  providers: [],
  exports: [TypeOrmModule],
})
export class DatabasePostgresModule {}
