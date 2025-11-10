import { envs } from '@hsm-lib/config';
import { DatabaseLogger } from '@hsm-lib/database/common/logger/database.logger';
import type { TypeOrmModuleOptions } from '@nestjs/typeorm';

export const DatabaseSourceOptions: Pick<
  TypeOrmModuleOptions,
  'retryAttempts' | 'retryDelay' | 'logging' | 'extra' | 'logger'
> = {
  logging: envs.ENVIRONMENT === 'dev' ? 'all' : ['error', 'warn'],
  logger: new DatabaseLogger(), //envs.ENVIRONMENT === 'dev' ? 'advanced-console' : 'formatted-console',
  extra: {
    poolMin: 2,
    poolMax: 10,
    poolIncrement: 1,
    poolTimeout: 60,
  },
  retryAttempts: 5,
  retryDelay: 1000,
};
