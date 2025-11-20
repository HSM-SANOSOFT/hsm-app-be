import { parseArgs } from 'node:util';
import { getOracleConnection } from '@hsm-lib/database/generator/datasources';
import { OracleEntityTables } from '@hsm-lib/database/generator/tables';
import {
  oracleColumnsMapping,
  oracleColumnsQuery,
  oracleConstrainsKeyMapping,
  oracleConstrainsMapping,
  oracleConstrainsQuery,
} from '@hsm-lib/database/generator/utils';
import { Logger } from '@nestjs/common';

async function oracleEntityGenerator() {
  const logger = new Logger('OracleEntityGenerator');
  const parsedValue = parseArgs({
    options: {
      user: { type: 'string' },
      pass: { type: 'string' },
      host: { type: 'string' },
      port: { type: 'string' },
      db: { type: 'string' },
      table: { type: 'string' },
      schema: { type: 'string' },
    },
  }).values;
  const missing: string[] = [];
  if (!parsedValue.user) missing.push('user');
  if (!parsedValue.pass) missing.push('password');
  if (!parsedValue.host) missing.push('host');
  if (!parsedValue.port) missing.push('port');
  if (!parsedValue.db) missing.push('database');
  if (!parsedValue.table) missing.push('table');
  if (!parsedValue.schema) missing.push('schema');
  if (missing.length > 0) {
    logger.error(
      `Missing oracle database arguments: ${missing.join(', ')}. ` +
        `Usage: pnpm db:gen:... --user=USER --password=PWD --host=HOST --port=1521 --database=DB`,
    );
    process.exit(1);
  }
  const args = parsedValue as Record<string, string>;
  const connection = await getOracleConnection(
    args.user,
    args.pass,
    args.host,
    args.port,
    args.db,
  );
  try {
    let tablesToGenerate: string[];

    if (args.table === 'all') {
      tablesToGenerate = OracleEntityTables;
    } else {
      tablesToGenerate = [args.table.toUpperCase()];
    }
    const schema = args.schema.toUpperCase();

    for (const tableName of tablesToGenerate) {
      logger.log(`Fetching DDL for ${schema}.${tableName}...`);

      const columnsResult = await oracleColumnsQuery(
        connection,
        schema,
        tableName,
      );
      // logger.debug(columnsResult);

      const columnsMapping = oracleColumnsMapping(columnsResult);

      const constrainsResult = await oracleConstrainsQuery(
        connection,
        schema,
        tableName,
      );
      // logger.debug(constrainsResult);

      const constrainsMapping = oracleConstrainsMapping(constrainsResult);
      // logger.debug(constrainsMapping);

      const [pkConstraints, fkConstraints, ukConstraints] =
        oracleConstrainsKeyMapping(constrainsMapping);
    }
  } catch (error) {
    logger.error('DDL extraction failed');
    logger.error(error);
    process.exit(1);
  } finally {
    try {
      await connection.close();
    } catch (error) {
      logger.error('Failed to close connection');
      logger.error(error);
      process.exit(1);
    }
  }
}
