import { envs } from '@hsm-lib/config';
import { Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';
import { Databases } from '../database-source.enum';
import { DatabaseSourceOptions } from '../database-source-options';
import { databasePostgresEntities } from './database-postgres.entities';

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
      entities: databasePostgresEntities,
      ...DatabaseSourceOptions,
    }),
    TypeOrmModule.forFeature(databasePostgresEntities, Databases.HsmDbPostgres),
  ],
  controllers: [],
  providers: [],
  exports: [TypeOrmModule],
})
export class DatabasePostgresModule {}
