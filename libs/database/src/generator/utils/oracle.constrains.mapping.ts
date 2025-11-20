import {
  OracleConstrains,
  OracleConstrainsQuery,
} from '@hsm-lib/database/generator/definitions';
import oracledb from 'oracledb';

export function oracleConstrainsMapping(
  constrainsResult: oracledb.Result<OracleConstrainsQuery>,
): OracleConstrains[] {
  const constrainsRows = constrainsResult.rows ?? [];
  return constrainsRows.map(constrain => ({
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
}
