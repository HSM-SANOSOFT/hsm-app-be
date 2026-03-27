import { IUnsignedUser } from '@hsm-lib/common/definitions/interfaces';

export interface ISignedUser extends IUnsignedUser {
  iat: number;
  exp: number;
}
