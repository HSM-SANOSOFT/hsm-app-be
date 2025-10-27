import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { Request } from 'express';
import { ExtractJwt, Strategy } from 'passport-jwt';

import { envs } from '@hsm-lib/config';
import { IJwtPayload, ISignedUser } from '@hsm-lib/definitions/interfaces';

@Injectable()
export class AuthJwtRTStrategy extends PassportStrategy(Strategy) {
  constructor() {
    super({
      jwtFromRequest: ExtractJwt.fromAuthHeaderAsBearerToken(),
      ignoreExpiration: false,
      secretOrKey: envs.JWT_RT_SECRET,
      passReqToCallback: true,
    });
  }

  async validate(req: Request, payload: IJwtPayload) {
    const refreshToken = req
      .get('Authorization')
      ?.replace('Bearer ', '')
      .trim();
    const { sub, ...rest } = payload;
    const user: ISignedUser = { id: sub, ...rest };
    return { user, refresh_token: refreshToken }; 
  }
}
