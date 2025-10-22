import type { IUser } from '@hsm-lib/definitions/interfaces/modules/core/users';

export interface JwtPayload extends Omit<IUser, 'id' | 'password'> {
  sub: IUser['id'];
}
