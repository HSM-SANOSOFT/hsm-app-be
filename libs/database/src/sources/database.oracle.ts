import { envs } from '@hsm-lib/config';
import { TypeOrmModule } from '@nestjs/typeorm';

import { Databases } from './database.enum';

export const DatabaseSourceOracle = TypeOrmModule.forRoot({
  type: 'oracle',
  name: Databases.HsmDbOracle,
  host: envs.HSM_DB_ORACLE_HOST,
  port: envs.HSM_DB_ORACLE_PORT,
  username: envs.HSM_DB_ORACLE_USER,
  password: envs.HSM_DB_ORACLE_PASSWORD,
  database: envs.HSM_DB_ORACLE_DB,
  synchronize: false,
  logging: true,
  logger: 'advanced-console',
  retryAttempts: 5,
  retryDelay: 1000,
  entities: [],
});
