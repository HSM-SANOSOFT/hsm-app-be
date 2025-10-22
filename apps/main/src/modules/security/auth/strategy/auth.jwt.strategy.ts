import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { ExtractJwt, Strategy } from 'passport-jwt';

import { envs } from '@hsm-lib/config';
import { IUser } from '@hsm-lib/definitions/interfaces';
import { JwtPayload } from '@hsm-lib/definitions/types/modules/security/auth';

@Injectable()
export class AuthJwtStrategy extends PassportStrategy(Strategy) {
  constructor() {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: envs.JWT_SECRET,
    });
  }

  async validate(payload: JwtPayload) {
    const { sub, ...rest } = payload;
    const user: Omit<IUser, 'password'> = { id: sub, ...rest };
    return user;
  }
}
