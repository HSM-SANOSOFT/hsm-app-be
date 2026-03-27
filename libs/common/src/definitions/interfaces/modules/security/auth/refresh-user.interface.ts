import {
  ISignedUser,
  ISignedUserIntegration,
} from '@hsm-lib/common/definitions/interfaces';

export type IRefreshUser = (ISignedUser | ISignedUserIntegration) & {
  refreshToken: string;
};
