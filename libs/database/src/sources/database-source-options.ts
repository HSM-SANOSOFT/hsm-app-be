import { envs } from '@hsm-lib/config';

import type { TypeOrmModuleOptions } from '@nestjs/typeorm';

export const DatabaseSourceOptions: Pick<
  TypeOrmModuleOptions,
  'retryAttempts' | 'retryDelay' | 'logging' | 'extra' | 'logger'
> = {
  logging: true,
  logger: envs.ENVIRONMENT === 'dev' ? 'debug' : 'simple-console',
  extra: {
    poolMin: 2,
    poolMax: 10,
    poolIncrement: 1,
    poolTimeout: 60,
  },
  retryAttempts: 5,
  retryDelay: 1000,
};
