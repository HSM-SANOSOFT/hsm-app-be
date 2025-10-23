import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { ExtractJwt, Strategy } from 'passport-jwt';

import { envs } from '@hsm-lib/config';
import { IJwtPayload, ISignedUser } from '@hsm-lib/definitions/interfaces';

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

  async validate(payload: IJwtPayload) {
    const { sub, ...rest } = payload;
    const user: ISignedUser = { id: sub, ...rest };
    return user;
  }
}
