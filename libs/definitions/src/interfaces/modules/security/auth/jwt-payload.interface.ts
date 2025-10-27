import type {
  UserEntity,
  UserIntegrationEntity,
} from '@hsm-lib/database/entities';
import type { Roles } from '@hsm-lib/definitions/types';
export interface IJwtPayloadUser
  extends Pick<UserEntity, 'username' | 'email' | 'roles'> {
  sub: UserEntity['id'];
}

export interface IJwtPayloadUserIntegration
  extends Pick<UserIntegrationEntity, 'name' | 'description'|"functionality"> {
  sub: UserIntegrationEntity['id'];
  roles: Roles[];
}
