import { DatabaseUniversalSchemasEnum } from '../database-universal.schemas';

enum SchemasEnum {
  USERS = 'users',
  AUTH = 'auth',
  DOCS = 'docs',
  TEMPLATES = 'templates',
}

export const DatabasePostgresSchemasEnum = {
  ...DatabaseUniversalSchemasEnum,
  ...SchemasEnum,
} as const;

export type DatabasePostgresSchemasEnum =
  (typeof DatabasePostgresSchemasEnum)[keyof typeof DatabasePostgresSchemasEnum];
