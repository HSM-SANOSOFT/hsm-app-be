import { isEntity } from '../../common/utils';
import * as docsEntities from '../../entities/modules/core/docs';
import * as userEntities from '../../entities/modules/core/users';
import * as authEntities from '../../entities/modules/security/auth';
import { databaseUniversalEntities } from '../database-universal.entities';

export const databasePostgresEntities = [
  ...databaseUniversalEntities,
  ...Object.values(authEntities),
  ...Object.values(userEntities),
  ...Object.values(docsEntities),
].filter(isEntity);
