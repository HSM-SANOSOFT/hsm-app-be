import { OracleConstrainsQuery } from '@hsm-lib/database/generator/definitions';
import oracledb from 'oracledb';

export async function oracleConstrainsQuery(
  connection: oracledb.Connection,
  schema: string,
  tableName: string,
): Promise<oracledb.Result<OracleConstrainsQuery>> {
  return await connection.execute<OracleConstrainsQuery>(
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
}
