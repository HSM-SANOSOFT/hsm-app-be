import { IUnsignedUserIntegration } from '@hsm-lib/common/interfaces';
export interface ISignedUserIntegration extends IUnsignedUserIntegration {
  iat: number;
  exp: number;
}
