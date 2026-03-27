import { UserEntity } from '@hsm-lib/database/entities';
import type { Roles } from '@hsm-lib/definitions/types';

export interface IUnsignedUser
  extends Pick<
    UserEntity,
    'id' | 'username' | 'email' | 'firstName' | 'firstLastName'
  > {
  roles: Roles[];
}
