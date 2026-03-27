import type { Roles } from '@hsm-lib/common/definitions/types';
import { UserIntegrationEntity } from '@hsm-lib/database/entities';

export interface IUnsignedUserIntegration
  extends Pick<UserIntegrationEntity, 'id' | 'name'> {
  roles: Roles[];
}
