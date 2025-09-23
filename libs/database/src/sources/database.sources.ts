import { DatabaseSourceOracle } from './database.oracle';
import { DatabaseSourcePostgres } from './database.postgres';

export const DatabaseSources = [
  DatabaseSourcePostgres,
  DatabaseSourceOracle,
] as const;
