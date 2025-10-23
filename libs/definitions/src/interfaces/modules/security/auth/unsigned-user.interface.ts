import type { IUser } from '@hsm-lib/definitions/interfaces';

export type IUnsignedUser = Omit<IUser, 'password'>;
