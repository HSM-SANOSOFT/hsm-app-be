import { envs } from '@hsm-lib/config';
import { IUnsuccessResponse } from '@hsm-lib/definitions/interfaces';
import {
  ArgumentsHost,
  Catch,
  ExceptionFilter,
  HttpStatus,
  Logger,
} from '@nestjs/common';
import { Response } from 'express';
import * as Mssql from 'mssql';
import * as Mysql from 'mysql2';
import * as Oracle from 'oracledb';
import * as Pg from 'pg';
import { QueryFailedError } from 'typeorm';

@Catch(QueryFailedError)
export class TypeOrmExceptionFilter implements ExceptionFilter {
  private readonly logger = new Logger('DatabaseExceptionFilter');

  catch(exception: QueryFailedError, host: ArgumentsHost) {
    const ctx = host.switchToHttp();
    const response: Response = ctx.getResponse();

    const statusCode = HttpStatus.INTERNAL_SERVER_ERROR;
    const message = 'Database query failed';

    type PostgresError = Pg.DatabaseError;
    type OracleError = Oracle.DBError;
    type MysqlError = Mysql.QueryError;
    type MssqlError = Mssql.RequestError;
    type DriverError =
      | PostgresError
      | OracleError
      | MysqlError
      | MssqlError
      | Record<string, unknown>;

    const driverError: DriverError =
      exception.driverError as unknown as DriverError;
    const pick = (...keys: PropertyKey[]): string | undefined => {
      const v = keys
        .map(k => (driverError as Record<PropertyKey, unknown>)[k])
        .find(v => v != null && v !== '');
      return v == null ? undefined : typeof v === 'string' ? v : String(v);
    };

    const errorInfo: {
      query?: string | undefined;
      parameters?: unknown | undefined;
      code?: string | undefined;
      detail?: string | undefined;
      err: string;
      schema?: string | undefined;
      table?: string | undefined;
    } = {
      query: envs.ENVIRONMENT === 'development' ? exception.query : undefined,
      parameters:
        envs.ENVIRONMENT === 'development' ? exception.parameters : undefined,
      err: exception.message,
      code: pick('errorNum', 'errno', 'code')?.toString(),
      detail: pick('detail', 'sqlMessage', 'message'),
      schema: pick('schema', 'serverName'),
      table: pick('table', 'procName'),
    };
    this.logger.error(`${message}: ${JSON.stringify(errorInfo)}`);

    const issue: IUnsuccessResponse = {
      issue: {
        detail: errorInfo.detail || errorInfo.err,
        code: errorInfo.code,
        message,
        field: `${errorInfo.table}: ${errorInfo.schema}`,
      },
    };

    response.status(statusCode).json({
      statusCode,
      error: issue,
    });
  }
}
