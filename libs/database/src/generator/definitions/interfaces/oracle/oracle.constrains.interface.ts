import { OracleConstrainsQuery } from '@hsm-lib/database/generator/definitions';

export interface OracleConstrains
  extends Pick<
    OracleConstrainsQuery,
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
  > {}
