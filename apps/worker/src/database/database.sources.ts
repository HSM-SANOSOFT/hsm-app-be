import type { DataSourceOptions } from 'typeorm';

import { envs } from '../config';

export const DatabaseSources: DataSourceOptions[] = [
  {
    type: 'postgres',
    name: 'hsm-db-postgres',
    host: envs.HSM_DB_POSTGRES_HOST,
    port: envs.HSM_DB_POSTGRES_PORT,
    username: envs.HSM_DB_POSTGRES_USER,
    password: envs.HSM_DB_POSTGRES_PASSWORD,
    database: envs.HSM_DB_POSTGRES_DB,
    synchronize: true,
  },
  {
    type: 'oracle',
    name: 'hsm-db-oracle',
    host: envs.HSM_DB_ORACLE_HOST,
    port: envs.HSM_DB_ORACLE_PORT,
    username: envs.HSM_DB_ORACLE_USER,
    password: envs.HSM_DB_ORACLE_PASSWORD,
    database: envs.HSM_DB_ORACLE_DB,
    synchronize: false,
  },
];
