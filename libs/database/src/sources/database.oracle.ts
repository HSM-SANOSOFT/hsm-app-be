import { envs } from '@hsm-lib/config';
import { Logger } from '@nestjs/common';
import oracledb from 'oracledb';

import { Databases } from './database.enum';

export const DatabaseProviderOracle = {
  provide: Databases.HsmDbOracle,
  useFactory: async () => {
    const logger = new Logger('OracleDB');
    try {
      oracledb.initOracleClient({
        libDir: '',
      });
      (oracledb.fetchAsString as unknown) = [oracledb.CLOB];
      (oracledb.fetchAsBuffer as unknown) = [oracledb.BLOB];

      const pool = oracledb.createPool({
        user: envs.HSM_DB_ORACLE_USER,
        password: envs.HSM_DB_ORACLE_PASSWORD,
        connectString: `${envs.HSM_DB_ORACLE_HOST}:${envs.HSM_DB_ORACLE_PORT}/${envs.HSM_DB_ORACLE_DB}`,
        poolMin: 2,
        poolMax: 10,
        poolIncrement: 1,
        poolTimeout: 60,
      });
      logger.log('Oracle connection pool established successfully.');
      return pool;
    } catch (error) {
      logger.error('Error establishing Oracle connection pool:', error);
      throw error;
    }
  },
};

import { Module } from '@nestjs/common';
@Module({
  providers: [DatabaseProviderOracle],
  exports: [DatabaseProviderOracle],
})
export class DatabaseSourceOracle {}
