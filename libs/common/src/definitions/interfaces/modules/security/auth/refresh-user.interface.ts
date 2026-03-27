import {
  ISignedUser,
  ISignedUserIntegration,
} from '@hsm-lib/definitions/interfaces';

export type IRefreshUser = (ISignedUser | ISignedUserIntegration) & {
  refreshToken: string;
};
