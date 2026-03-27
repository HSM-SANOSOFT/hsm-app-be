import { IUnsignedUser } from '@hsm-lib/definitions/interfaces';

export interface ISignedUser extends IUnsignedUser {
  iat: number;
  exp: number;
}
