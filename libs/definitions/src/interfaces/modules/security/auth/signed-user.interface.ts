import type { UserEntity } from '@hsm-lib/database/entities';

export type ISignedUser = Pick<
  UserEntity,
  'id' | 'username' | 'email' | 'roles'
>;
