import { envs } from '@hsm-lib/config';
import { Logger, Module, OnModuleInit } from '@nestjs/common';
import { InjectDataSource, TypeOrmModule } from '@nestjs/typeorm';
import { DataSource } from 'typeorm';
import { DatabasesEnum } from '../database-source.enum';
import { DatabaseSourceOptions } from '../database-source-options';
import { databasePostgresEntities } from './database-postgres.entities';
import { DatabasePostgresSchemasEnum } from './database-postgres.schemas';
@Module({
  imports: [
    TypeOrmModule.forRoot({
      type: 'postgres',
      name: DatabasesEnum.HsmDbPostgres,
      host: envs.HSM_DB_POSTGRES_HOST,
      port: envs.HSM_DB_POSTGRES_PORT,
      username: envs.HSM_DB_POSTGRES_USER,
      password: envs.HSM_DB_POSTGRES_PASSWORD,
      database: envs.HSM_DB_POSTGRES_DB,
      synchronize: false,
      entities: databasePostgresEntities,
      ...DatabaseSourceOptions,
    }),
    TypeOrmModule.forFeature(
      databasePostgresEntities,
      DatabasesEnum.HsmDbPostgres,
    ),
  ],
  controllers: [],
  providers: [],
  exports: [TypeOrmModule],
})
export class DatabasePostgresModule implements OnModuleInit {
  private readonly logger = new Logger(DatabasePostgresModule.name);
  constructor(
    @InjectDataSource(DatabasesEnum.HsmDbPostgres)
    private dataSource: DataSource,
  ) {}

  async onModuleInit() {
    this.logger.debug('Ensuring schemas exist...');
    const schemas = Object.values(DatabasePostgresSchemasEnum);
    for (const schema of schemas) {
      await this.dataSource.query(`CREATE SCHEMA IF NOT EXISTS "${schema}"`);
      this.logger.debug(`Schema "${schema}" ensured`);
    }

    if (envs.ENVIRONMENT === 'dev') {
      this.logger.debug('Synchronizing database schema...');
      await this.dataSource.synchronize();
      this.logger.debug('Database schema synchronized');
    }
  }
}
