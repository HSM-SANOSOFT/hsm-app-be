import { DatabaseUniversalSchemasEnum } from '../database-universal.schemas';

enum SchemasEnum {
  SIS = 'SIS',
}

export const DatabaseOracleSchemasEnum = {
  ...DatabaseUniversalSchemasEnum,
  ...SchemasEnum,
} as const;

export type DatabaseOracleSchemasEnum =
  (typeof DatabaseOracleSchemasEnum)[keyof typeof DatabaseOracleSchemasEnum];
