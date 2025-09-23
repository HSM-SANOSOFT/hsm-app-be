import type { DynamicModule } from '@nestjs/common';

import { DatabaseSourceOracle } from './database.oracle';
import { DatabaseSourcePostgres } from './database.postgres';

export const DatabaseSources: ReadonlyArray<DynamicModule> = [
  DatabaseSourcePostgres,
  DatabaseSourceOracle,
] as const;
