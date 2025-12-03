// noinspection JSUnusedGlobalSymbols

import { IS_PUBLIC_KEY } from '@hsm-lib/common/decorator';
import { envs } from '@hsm-lib/config';
import {
  type ExecutionContext,
  Injectable,
  UnauthorizedException,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { JsonWebTokenError, TokenExpiredError } from '@nestjs/jwt';
import { AuthGuard } from '@nestjs/passport';

@Injectable()
export class AuthJwtAtGuard extends AuthGuard('jwt-at') {
  constructor(private reflector: Reflector) {
    super();
  }

  handleRequest(err, user, info) {
    if (info instanceof TokenExpiredError) {
      throw new UnauthorizedException('token expired', 'TOKEN_EXPIRED');
    }

    if (info instanceof JsonWebTokenError) {
      throw new UnauthorizedException('Invalid token', 'INVALID_TOKEN');
    }

    if (err || !user) {
      throw err || new UnauthorizedException();
    }

    return user;
  }

  canActivate(context: ExecutionContext) {
    const isPublic = this.reflector.getAllAndOverride<boolean>(IS_PUBLIC_KEY, [
      context.getHandler(),
      context.getClass(),
    ]);
    if (envs.ENVIRONMENT === 'dev') {
      return true;
    }
    if (isPublic) {
      return true;
    }
    return super.canActivate(context);
  }
}
