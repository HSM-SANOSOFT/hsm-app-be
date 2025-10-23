import type { IUser } from '@hsm-lib/definitions/interfaces/modules/core/users';

export interface IJwtPayload extends Omit<IUser, 'id' | 'password' | 'name' | 'surname'> {
  sub: IUser['id'];
}
