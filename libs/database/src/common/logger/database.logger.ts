import { Logger as NestLogger } from '@nestjs/common';
import { QueryRunner, Logger as TypeOrmLogger } from 'typeorm';

export class DatabaseLogger implements TypeOrmLogger {
  private readonly logger = new NestLogger('DatabaseSource');
  private readonly needTrimming = true;
  private readonly lengthLimit = 500;
  private readonly logParameters = true;

  stringParce(query: string): string {
    if (this.needTrimming && query.length > this.lengthLimit) {
      return `${query.slice(0, this.lengthLimit)}...`;
    }
    return query;
  }

  paramParce(parameters: unknown[] | undefined): string {
    if (!this.logParameters) return '';
    if (!parameters) return '';
    const paramsString = this.stringParce(JSON.stringify(parameters));
    return paramsString;
  }

  logQuery(query: string, parameters?: unknown[], _queryRunner?: QueryRunner) {
    const queryPart = this.stringParce(query);
    const paramsPart = this.paramParce(parameters);
    this.logger.debug(`Query: ${queryPart} -- Parameters: ${paramsPart}`);
  }

  logQueryError(
    error: string | Error,
    query: string,
    parameters?: unknown[],
    _queryRunner?: QueryRunner,
  ) {
    const queryPart = this.stringParce(query);
    const paramsPart = this.paramParce(parameters);
    this.logger.error(
      `Query Failed: ${queryPart} -- Parameters: ${paramsPart} -- Error: ${error}`,
    );
  }

  logQuerySlow(
    time: number,
    query: string,
    parameters?: unknown[],
    _queryRunner?: QueryRunner,
  ) {
    const queryPart = this.stringParce(query);
    const paramsPart = this.paramParce(parameters);
    this.logger.warn(
      `Slow Query (${time}ms): ${queryPart} -- Parameters: ${paramsPart}`,
    );
  }

  logSchemaBuild(message: string, _queryRunner?: QueryRunner) {
    const messagePart = this.stringParce(message);
    this.logger.debug(`Schema: ${messagePart}`);
  }

  logMigration(message: string, _queryRunner?: QueryRunner) {
    const messagePart = this.stringParce(message);
    this.logger.debug(`Migration: ${messagePart}`);
  }

  log(
    level: 'log' | 'info' | 'warn',
    message: unknown,
    _queryRunner?: QueryRunner,
  ) {
    switch (level) {
      case 'log':
      case 'info':
        this.logger.log(message);
        break;
      case 'warn':
        this.logger.warn(message);
        break;
    }
  }
}
