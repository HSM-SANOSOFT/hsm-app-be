import { Logger, Module } from '@nestjs/common';
import oracledb from 'oracledb';

import { envs } from '@hsm-lib/config';

import { Databases } from './database.enum';

export const DatabaseProviderOracle2 = {
  provide: Databases.HsmDbOracle,
  useFactory: async () => {
    const logger = new Logger('OracleDB');
    try {
      oracledb.initOracleClient({
        libDir: '/usr/lib/oracle/12.1/client64/lib',
      });
      (oracledb.fetchAsString) = [oracledb.CLOB];
      (oracledb.fetchAsBuffer) = [oracledb.BLOB];

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
    }
    catch (error) {
      logger.error('Error establishing Oracle connection pool:', error);
      throw error;
    }
  },
};

@Module({
  providers: [DatabaseProviderOracle2],
  exports: [DatabaseProviderOracle2],
})
export class DatabaseSourceOracle {}
