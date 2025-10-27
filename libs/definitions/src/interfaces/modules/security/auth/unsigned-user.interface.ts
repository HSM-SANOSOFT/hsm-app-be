import type { UserEntity } from '@hsm-lib/database/entities';

export type IUnsignedUser = Omit<UserEntity, 'password'>;
