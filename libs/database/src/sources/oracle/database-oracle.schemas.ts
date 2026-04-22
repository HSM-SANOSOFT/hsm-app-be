import { DatabaseUniversalSchemasEnum } from '../database-universal.schemas';

enum SchemasEnum {
  USERS = 'users',
  AUTH = 'auth',
  DOCS = 'docs',
  TEMPLATES = 'templates',
}

export const DatabaseOracleSchemasEnum = {
  ...DatabaseUniversalSchemasEnum,
  ...SchemasEnum,
} as const;

export type DatabaseOracleSchemasEnum =
  (typeof DatabaseOracleSchemasEnum)[keyof typeof DatabaseOracleSchemasEnum];
