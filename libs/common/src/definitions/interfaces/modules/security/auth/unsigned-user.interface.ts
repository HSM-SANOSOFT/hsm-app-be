import type { Roles } from '@hsm-lib/common/definitions/types';
import { UserEntity } from '@hsm-lib/database/entities';

export interface IUnsignedUser
  extends Pick<
    UserEntity,
    'id' | 'username' | 'email' | 'firstName' | 'firstLastName'
  > {
  roles: Roles[];
}
