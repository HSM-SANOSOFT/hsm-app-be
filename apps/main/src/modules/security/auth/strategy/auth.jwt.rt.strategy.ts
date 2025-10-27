import { envs } from '@hsm-lib/config';
import type { IJwtPayloadUser, IJwtPayloadUserIntegration } from '@hsm-lib/definitions/interfaces';
import { Injectable } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { Request } from 'express';
import { ExtractJwt, Strategy } from 'passport-jwt';

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

  validate(req: Request, payload: IJwtPayloadUser|IJwtPayloadUserIntegration) {
    const refreshToken = req
      .get('Authorization')
      ?.replace('Bearer ', '')
      .trim();
    const { sub, ...rest } = payload;
    const user = { id: sub, ...rest };
    return { user, refresh_token: refreshToken };
  }
}
