import {
  ISignedUser,
  ISignedUserIntegration,
} from '@hsm-lib/common/interfaces';

export type IRefreshUser = (ISignedUser | ISignedUserIntegration) & {
  refreshToken: string;
};
