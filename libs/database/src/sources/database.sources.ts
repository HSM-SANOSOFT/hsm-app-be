// import { DatabaseSourceOracle } from './database.oracle';
// import { DatabaseSourcePostgres } from './database.postgres';

import { DatabaseSourceOracle } from './database.oracle';

export const DatabaseSources = [
  // DatabaseSourcePostgres,
  // DatabaseSourceOracle2,
  DatabaseSourceOracle,
] as const;
