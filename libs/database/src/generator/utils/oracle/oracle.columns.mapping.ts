import {
  OracleColumns,
  OracleColumnsQuery,
} from '@hsm-lib/database/generator/definitions';
import oracledb from 'oracledb';

export function oracleColumnsMapping(
  columnsResult: oracledb.Result<OracleColumnsQuery>,
): OracleColumns[] {
  const columnsRows = columnsResult.rows ?? [];
  return columnsRows.map(column => ({
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
    COMMENTS: column.COMMENTS,
  }));
}
