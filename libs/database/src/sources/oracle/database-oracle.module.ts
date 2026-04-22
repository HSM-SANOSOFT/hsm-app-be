import { envs } from '@hsm-lib/config';
import { Logger, Module } from '@nestjs/common';
import { TypeOrmModule } from '@nestjs/typeorm';
import oracledb from 'oracledb';
import { DatabasesEnum } from '../database-source.enum';
import { DatabaseSourceOptions } from '../database-source-options';
import { databaseOracleEntities } from './database-oracle.entities';

try {
  oracledb.initOracleClient({
    libDir: '/usr/lib/oracle/12.1/client64/lib',
  });
  oracledb.fetchAsString = [oracledb.CLOB];
  oracledb.fetchAsBuffer = [oracledb.BLOB];
} catch (err) {
  Logger.error('Error initializing Oracle client', err);
}

@Module({
  imports: [
    TypeOrmModule.forRoot({
      type: 'oracle',
      name: DatabasesEnum.HsmDbOracle,
      host: envs.HSM_DB_ORACLE_HOST,
      port: envs.HSM_DB_ORACLE_PORT,
      username: envs.HSM_DB_ORACLE_USER,
      password: envs.HSM_DB_ORACLE_PASSWORD,
      connectString: `${envs.HSM_DB_ORACLE_HOST}:${envs.HSM_DB_ORACLE_PORT}/${envs.HSM_DB_ORACLE_DB}`,
      synchronize: false,
      entities: databaseOracleEntities,
      ...DatabaseSourceOptions,
    }),
    TypeOrmModule.forFeature(databaseOracleEntities, DatabasesEnum.HsmDbOracle),
  ],
  controllers: [],
  providers: [],
  exports: [TypeOrmModule],
})
export class DatabaseOracleModule {}
