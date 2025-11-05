import { IUnsignedUserIntegration } from '@hsm-lib/definitions/interfaces';
export interface ISignedUserIntegration extends IUnsignedUserIntegration {
  iat: number;
  exp: number;
}
