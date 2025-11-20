import { OracleColumnsQuery} from '@hsm-lib/database/generator/definitions';

export interface OracleColumns
  extends Pick<
    OracleColumnsQuery,
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
    | 'COMMENTS'
  > {}
