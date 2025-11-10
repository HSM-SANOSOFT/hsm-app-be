import { Logger as NestLogger } from '@nestjs/common';
import { QueryRunner, Logger as TypeOrmLogger } from 'typeorm';

export class DatabaseLogger implements TypeOrmLogger {
  private readonly logger = new NestLogger('DatabaseSource');

  logQuery(query: string, parameters?: unknown[], _queryRunner?: QueryRunner) {
    const paramsPart = parameters
      ? ` -- Parameters: ${JSON.stringify(parameters)}`
      : '';
    this.logger.debug(`Query: ${query}${paramsPart}`);
  }

  logQueryError(
    error: string | Error,
    query: string,
    parameters?: unknown[],
    _queryRunner?: QueryRunner,
  ) {
    const paramsPart = parameters
      ? ` -- Parameters: ${JSON.stringify(parameters)}`
      : '';
    this.logger.error(`Query Failed: ${query}${paramsPart} -- Error: ${error}`);
  }

  logQuerySlow(
    time: number,
    query: string,
    parameters?: unknown[],
    _queryRunner?: QueryRunner,
  ) {
    const paramsPart = parameters
      ? ` -- Parameters: ${JSON.stringify(parameters)}`
      : '';
    this.logger.warn(`Slow Query (${time}ms): ${query}${paramsPart}`);
  }

  logSchemaBuild(message: string, _queryRunner?: QueryRunner) {
    this.logger.debug(`Schema: ${message}`);
  }

  logMigration(message: string, _queryRunner?: QueryRunner) {
    this.logger.debug(`Migration: ${message}`);
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
