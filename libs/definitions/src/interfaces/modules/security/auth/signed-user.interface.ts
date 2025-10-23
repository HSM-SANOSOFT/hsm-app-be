import type { IUser } from '@hsm-lib/definitions/interfaces';

export type ISignedUser = Omit<IUser, 'password' | 'name' | 'surname'>;
