import { TypeOrmModule } from '@nestjs/typeorm';
import type { DataSourceOptions } from 'typeorm';

import { DatabaseSources } from './database.sources';

export const DatabaseProvider = DatabaseSources.map(
  (options: DataSourceOptions) => {
    return TypeOrmModule.forRoot({
      ...options,
      autoLoadEntities: true,
      logging: true,
      logger: 'advanced-console',
      retryAttempts: 5,
      retryDelay: 1000,
    });
  },
);
