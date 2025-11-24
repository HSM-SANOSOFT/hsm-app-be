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

export async function oracleEntityGenerator(args: {
  user: string;
  pass: string;
  host: string;
  port: string;
  db: string;
  table: string;
  schema: string;
  log?: boolean;
}) {
  const logger = new Logger('OracleEntityGenerator');
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
