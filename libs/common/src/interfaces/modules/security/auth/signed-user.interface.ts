import { IUnsignedUser } from '@hsm-lib/common/interfaces';

export interface ISignedUser extends IUnsignedUser {
  iat: number;
  exp: number;
}
