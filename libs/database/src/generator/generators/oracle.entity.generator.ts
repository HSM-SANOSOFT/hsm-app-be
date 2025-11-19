import { parseArgs } from 'node:util';
import { getOracleConnection } from '@hsm-lib/database/generator/datasources';
import { OracleEntityTables } from '@hsm-lib/database/generator/tables/oracle.entity.tables';
import { Logger } from '@nestjs/common';

enum oracleConstrainType {
  C = 'C', //Check constraint on a table
  P = 'P', //Primary key
  U = 'U', //Unique key
  R = 'R', //Referential integrity (foreign key)
  V = 'V', //With check option, on a view
  O = 'O', //With read only, on a view
  H = 'H', //Hash expression
  F = 'F', //Constraint that involves a REF column
  S = 'S', //Supplemental logging
}

interface ColumnQuery {
  OWNER: string | null;
  TABLE_NAME: string | null;
  COLUMN_NAME: string | null;
  DATA_TYPE: string | null;
  DATA_TYPE_MOD: unknown | null;
  DATA_TYPE_OWNER: unknown | null;
  DATA_LENGTH: number | null;
  DATA_PRECISION: number | null;
  DATA_SCALE: number | null;
  NULLABLE: string | null;
  COLUMN_ID: number | null;
  DEFAULT_LENGTH: unknown | null;
  DATA_DEFAULT: string | null;
  NUM_DISTINCT: number | null;
  DENSITY: number | null;
  NUM_NULLS: number | null;
  NUM_BUCKETS: number | null;
  LAST_ANALYZED: string | null;
  SAMPLE_SIZE: number | null;
  CHARACTER_SET_NAME: string | null;
  CHAR_COL_DECL_LENGTH: number | null;
  GLOBAL_STATS: string | null;
  USER_STATS: string | null;
  AVG_COL_LEN: number | null;
  CHAR_LENGTH: number | null;
  CHAR_USED: string | null;
  V80_FMT_IMAGE: string | null;
  DATA_UPGRADED: string | null;
  HISTOGRAM: string | null;
}

type Column = Pick<
  ColumnQuery,
  | 'OWNER'
  | 'TABLE_NAME'
  | 'COLUMN_NAME'
  | 'DATA_TYPE'
  | 'DATA_TYPE_MOD'
  | 'DATA_TYPE_OWNER'
  | 'DATA_LENGTH'
  | 'DATA_PRECISION'
  | 'DATA_SCALE'
  | 'DATA_DEFAULT'
  | 'COLUMN_ID'
  | 'NULLABLE'
>;

interface ConstrainQuery {
  OWNER: string | null;
  CONSTRAINT_NAME: string | null;
  CONSTRAINT_TYPE: string | null;
  TABLE_NAME: string | null;
  SEARCH_CONDITION: string | null;
  R_OWNER: string | null;
  R_CONSTRAINT_NAME: string | null;
  DELETE_RULE: unknown | null;
  STATUS: string | null;
  DEFERRABLE: string | null;
  DEFERRED: string | null;
  VALIDATED: string | null;
  GENERATED: string | null;
  BAD: unknown | null;
  RELY: unknown | null;
  LAST_CHANGE: string | null;
  INDEX_OWNER: unknown | null;
  INDEX_NAME: unknown | null;
  INVALID: unknown | null;
  VIEW_RELATED: unknown | null;
  OWNER_1: string | null;
  CONSTRAINT_NAME_1: string | null;
  TABLE_NAME_1: string | null;
  COLUMN_NAME: string | null;
  POSITION: unknown | null;
  OWNER_2: string | null;
  CONSTRAINT_NAME_2: string | null;
  CONSTRAINT_TYPE_1: string | null;
  TABLE_NAME_2: string | null;
  SEARCH_CONDITION_1: unknown | null;
  R_OWNER_1: string | null;
  R_CONSTRAINT_NAME_1: string | null;
  DELETE_RULE_1: unknown | null;
  STATUS_1: string | null;
  DEFERRABLE_1: string | null;
  DEFERRED_1: string | null;
  VALIDATED_1: string | null;
  GENERATED_1: string | null;
  BAD_1: unknown | null;
  RELY_1: unknown | null;
  LAST_CHANGE_1: string | null;
  INDEX_OWNER_1: unknown | null;
  INDEX_NAME_1: string | null;
  INVALID_1: unknown | null;
  VIEW_RELATED_1: unknown | null;
}

type Constrains = Pick<
  ConstrainQuery,
  | 'OWNER'
  | 'CONSTRAINT_NAME'
  | 'CONSTRAINT_TYPE'
  | 'TABLE_NAME'
  | 'COLUMN_NAME'
  | 'POSITION'
  | 'SEARCH_CONDITION'
  | 'STATUS'
  | 'R_OWNER'
  | 'R_CONSTRAINT_NAME'
  | 'TABLE_NAME_2'
>;

async function main() {
  const logger = new Logger('OracleDatasource');
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

      const columnsResult = await connection.execute<ColumnQuery>(
        `
                SELECT
                    *
                FROM ALL_TAB_COLUMNS
                WHERE OWNER = :owner
                  AND TABLE_NAME = :tableName
                ORDER BY COLUMN_ID
              `,
        { owner: schema, tableName },
      );

      logger.log(columnsResult);

      const columnsRows = columnsResult.rows ?? [];

      const columnDDL: Column[] = columnsRows.map(column => ({
        OWNER: column.OWNER,
        TABLE_NAME: column.TABLE_NAME,
        COLUMN_NAME: column.COLUMN_NAME,
        DATA_TYPE: column.DATA_TYPE,
        DATA_TYPE_MOD: column.DATA_TYPE_MOD,
        DATA_TYPE_OWNER: column.DATA_TYPE_OWNER,
        DATA_LENGTH: column.DATA_LENGTH,
        DATA_PRECISION: column.DATA_PRECISION,
        DATA_SCALE: column.DATA_SCALE,
        DATA_DEFAULT: column.DATA_DEFAULT,
        COLUMN_ID: column.COLUMN_ID,
        NULLABLE: column.NULLABLE,
      }));

      const constrainsResult = await connection.execute<ConstrainQuery>(
        `
            SELECT
                *
            FROM ALL_CONSTRAINTS ac
                     JOIN ALL_CONS_COLUMNS acc
                          ON ac.OWNER = acc.OWNER
                              AND ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME
                     LEFT JOIN ALL_CONSTRAINTS rcons
                               ON ac.R_OWNER = rcons.OWNER
                                   AND ac.R_CONSTRAINT_NAME = rcons.CONSTRAINT_NAME
            WHERE ac.OWNER = :owner
              AND ac.TABLE_NAME = :tableName
            ORDER BY acc.POSITION
        `,
        { owner: schema, tableName },
      );

      const constrainsRows = constrainsResult.rows ?? [];

      const constrainsDDL: Constrains[] = constrainsRows.map(constrain => ({
        OWNER: constrain.OWNER,
        CONSTRAINT_NAME: constrain.CONSTRAINT_NAME,
        CONSTRAINT_TYPE: constrain.CONSTRAINT_TYPE,
        TABLE_NAME: constrain.TABLE_NAME,
        COLUMN_NAME: constrain.COLUMN_NAME,
        POSITION: constrain.POSITION,
        SEARCH_CONDITION: constrain.SEARCH_CONDITION,
        STATUS: constrain.STATUS,
        R_OWNER: constrain.R_OWNER,
        R_CONSTRAINT_NAME: constrain.R_CONSTRAINT_NAME,
        TABLE_NAME_2: constrain.TABLE_NAME_2,
      }));

      const pkConstraints = new Map<string, string[]>();
      const fkConstraints = new Map<
        string,
        {
          columns: string[];
          referencedOwner: string | null;
          referencedConstraintName: string | null;
          referencedTable: string | null;
        }
      >();
      const ukConstraints = new Map<string, string[]>();

      for (const c of constrainsDDL) {
        if (!c.CONSTRAINT_NAME || !c.CONSTRAINT_TYPE || !c.COLUMN_NAME)
          continue;

        switch (c.CONSTRAINT_TYPE) {
          case oracleConstrainType.P: {
            const cols = pkConstraints.get(c.CONSTRAINT_NAME) ?? [];
            cols.push(c.COLUMN_NAME);
            pkConstraints.set(c.CONSTRAINT_NAME, cols);
            break;
          }

          case oracleConstrainType.U: {
            const cols = ukConstraints.get(c.CONSTRAINT_NAME) ?? [];
            cols.push(c.COLUMN_NAME);
            ukConstraints.set(c.CONSTRAINT_NAME, cols);
            break;
          }

          case oracleConstrainType.R: {
            const current = fkConstraints.get(c.CONSTRAINT_NAME) ?? {
              columns: [],
              referencedOwner: c.R_OWNER ?? null,
              referencedConstraintName: c.R_CONSTRAINT_NAME ?? null,
              referencedTable: c.TABLE_NAME_2 ?? null,
            };

            current.columns.push(c.COLUMN_NAME);
            current.referencedOwner = c.R_OWNER ?? current.referencedOwner;
            current.referencedConstraintName =
              c.R_CONSTRAINT_NAME ?? current.referencedConstraintName;
            current.referencedTable = c.TABLE_NAME_2 ?? current.referencedTable;

            fkConstraints.set(c.CONSTRAINT_NAME, current);
            break;
          }

          default:
            break;
        }
      }

      function formatDataType(column: Column): string {
        const type = column.DATA_TYPE;
        if (!type) return 'UNKNOWN';
        if (type === 'NUMBER') {
          const precision = column.DATA_PRECISION;
          const scale = column.DATA_SCALE;
          if (precision != null && scale != null) {
            return `NUMBER(${precision},${scale})`;
          }
          if (precision != null) {
            return `NUMBER(${precision})`;
          }
          return 'NUMBER';
        }
        if (column.DATA_LENGTH != null) {
          return `${type}(${column.DATA_LENGTH})`;
        }
        return type;
      }
      const columnLines = columnDDL
        .map(column => {
          const type = formatDataType(column);

          return (
            `${column.COLUMN_NAME}    ${type}` +
            (column.NULLABLE === 'N' ? ' not null' : '')
          );
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

      const constraintBlocks = [pkLines, ukLines, fkLines].filter(
        b => b.length > 0,
      );
      const constraintsSection = constraintBlocks.join(',\n');

      logger.log(
        '\n--- DDL START ---\n' +
          `\ncreate table ${tableName}\n` +
          `\n(\n` +
          columnLines +
          (constraintsSection ? ',\n' + constraintsSection : '') +
          `\n)\n` +
          '\n---  DDL END  ---\n',
      );
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

main();
