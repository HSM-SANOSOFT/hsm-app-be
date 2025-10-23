import { TypeOrmModule } from '@nestjs/typeorm';

import { envs } from '@hsm-lib/config';

import { UserEntity, UserRoleEntity } from '../entities/modules/core/users';
import { Databases } from './database.enum';

export const DatabaseSourcePostgres = TypeOrmModule.forRoot({
  type: 'postgres',
  name: Databases.HsmDbPostgres,
  host: envs.HSM_DB_POSTGRES_HOST,
  port: envs.HSM_DB_POSTGRES_PORT,
  username: envs.HSM_DB_POSTGRES_USER,
  password: envs.HSM_DB_POSTGRES_PASSWORD,
  database: envs.HSM_DB_POSTGRES_DB,
  synchronize: envs.ENVIRONMENT === 'dev',
  logging: true,
  logger: 'debug',
  retryAttempts: 5,
  retryDelay: 1000,
  entities: [UserEntity, UserRoleEntity],
});
