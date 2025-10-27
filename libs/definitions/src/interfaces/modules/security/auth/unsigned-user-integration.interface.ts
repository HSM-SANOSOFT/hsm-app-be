import type { UserIntegrationEntity } from '@hsm-lib/database/entities';
import type { Roles } from '@hsm-lib/definitions/types';

export interface IUnsignedUserIntegration extends UserIntegrationEntity {
  roles: Roles[];
}
