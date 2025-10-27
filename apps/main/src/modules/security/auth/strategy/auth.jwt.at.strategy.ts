import { envs } from '@hsm-lib/config';
import type {
  IJwtPayloadUser,
  IJwtPayloadUserIntegration,
} from '@hsm-lib/definitions/interfaces';
import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { ExtractJwt, Strategy } from 'passport-jwt';

@Injectable()
export class AuthJwtATStrategy extends PassportStrategy(Strategy) {
  constructor() {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: envs.JWT_AT_SECRET,
      passReqToCallback: false,
    });
  }

  validate(payload: IJwtPayloadUser | IJwtPayloadUserIntegration) {
    const { sub, ...rest } = payload;
    const user = { id: sub, ...rest };
    return user;
  }
}
