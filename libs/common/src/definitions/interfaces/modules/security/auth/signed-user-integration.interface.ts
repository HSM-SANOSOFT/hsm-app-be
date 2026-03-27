import { IUnsignedUserIntegration } from '@hsm-lib/common/definitions/interfaces';
export interface ISignedUserIntegration extends IUnsignedUserIntegration {
  iat: number;
  exp: number;
}
