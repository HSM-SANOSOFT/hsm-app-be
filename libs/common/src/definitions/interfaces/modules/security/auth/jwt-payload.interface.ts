import type {
  IUnsignedUser,
  IUnsignedUserIntegration,
} from '@hsm-lib/definitions/interfaces';

export interface IJwtPayloadUser extends Omit<IUnsignedUser, 'id'> {
  sub: IUnsignedUser['id'];
}

export interface IJwtPayloadUserIntegration
  extends Omit<IUnsignedUserIntegration, 'id'> {
  sub: IUnsignedUserIntegration['id'];
}
