import { getOracleConnection } from '@hsm-lib/database/generator/datasources';
import { OracleEntityTables } from '@hsm-lib/database/generator/tables';
import {
  oracleColumnsMapping,
  oracleColumnsQuery,
  oracleConstrainsKeyMapping,
  oracleConstrainsMapping,
  oracleConstrainsQuery,
  oracleDataTypeSchemaMapping,
} from '@hsm-lib/database/generator/utils';
import { Logger } from '@nestjs/common';
import * as fs from 'fs';
import * as path from 'path';

export async function oracleSchemaGenerator(args: {
  user: string;
  pass: string;
  host: string;
  port: string;
  db: string;
  table: string;
  schema: string;
  log?: boolean;
}) {
  const logger = new Logger('OracleSchemaGenerator');
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

      const columnLines = columnsMapping
        .map(column => {
          const type = oracleDataTypeSchemaMapping(column);

          const defaultValue = column.DATA_DEFAULT
            ? ` default ${column.DATA_DEFAULT.trim()}`
            : '';

          const notNull = column.NULLABLE === 'N' ? ' not null' : '';

          return `${column.COLUMN_NAME}    ${type}${defaultValue}${notNull}`;
        })
        .join(',\n');

      const pkLines = Array.from(pkConstraints.entries())
        .map(
          ([name, cols]) =>
            `constraint ${name}\n        primary key (${cols.join(', ')})`,
        )
        .join(',\n');

      const ukLines = Array.from(ukConstraints.entries())
        .map(
          ([name, cols]) =>
            `constraint ${name}\n        unique (${cols.join(', ')})`,
        )
        .join(',\n');

      const fkLines = Array.from(fkConstraints.entries())
        .map(([name, def]) => {
          const colsList = def.columns.join(', ');
          const refTable = def.referencedTable ?? '/* UNKNOWN_TABLE */';
          return `constraint ${name}\n        foreign key (${colsList}) references ${refTable}`;
        })
        .join(',\n');

      const commentLines = columnsMapping
        .map(col =>
          col.COMMENTS
            ? `comment on column ${tableName}.${col.COLUMN_NAME} is '${col.COMMENTS.replace(/'/g, "''")}';`
            : '',
        )
        .filter(line => line !== '')
        .join('\n');

      // logger.debug(commentLines);

      const constraintBlocks = [pkLines, ukLines, fkLines].filter(
        b => b.length > 0,
      );
      const constraintsSection = constraintBlocks.join(',\n');

      const ddl =
        '\n--- DDL START ---\n' +
        `\ncreate table ${tableName}\n` +
        `\n(\n` +
        columnLines +
        (constraintsSection ? ',\n' + constraintsSection : '') +
        `\n)\n` +
        (commentLines ? '\n' + '/\n' + commentLines + '\n' : '') +
        '\n---  DDL END  ---\n';
      if (args.log) {
        logger.log(ddl);
      }

      const outputDir = path.join(__dirname, '../schemas/oracle');

      if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
      }

      const filePath = path.join(outputDir, `${tableName.toLowerCase()}.sql`);
      fs.writeFileSync(filePath, ddl, 'utf8');

      logger.log(`Saved → ${filePath}`);
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
