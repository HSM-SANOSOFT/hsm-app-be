import type {
  IJwtPayloadUser,
  IJwtPayloadUserIntegration,
  IRefreshUser,
  ISignedUser,
  ISignedUserIntegration,
} from '@hsm/common/interfaces';
import { IUnsignedUser } from '@hsm/common/interfaces';
import { envs } from '@hsm/config/api';
import { Injectable, Logger } from '@nestjs/common';
import { PassportStrategy } from '@nestjs/passport';
import { Request } from 'express';
import {
  ExtractJwt,
  type JwtFromRequestFunction,
  Strategy as StrategyJwt,
} from 'passport-jwt';
import { Strategy } from 'passport-local';
import {
  ACCESS_COOKIE,
  REFRESH_COOKIE,
  cookieTokenExtractor,
  readCookie,
} from './auth-cookie.util';
import { AuthService } from './auth.service';

/**
 * Local Authentication Strategy
 * Validates user credentials (username and password) for local authentication.
 * Uses Passport's LocalStrategy to handle authentication logic.
 * Integrates with AuthService to validate user credentials against stored data.
 */
@Injectable()
export class AuthLocalStrategy extends PassportStrategy(Strategy, 'local') {
  constructor(private authService: AuthService) {
    super();
  }

  /**
   * Validates the user credentials (username and password).
   * @param username - The username provided by the client
   * @param password - The password provided by the client
   * @returns A promise that resolves to an unsigned user object if valid; otherwise throws an exception
   */
  async validate(username: string, password: string): Promise<IUnsignedUser> {
    return await this.authService.validateUser(username, password);
  }
}

/**
 * Dual-transport JWT extractors (U2): try the httpOnly cookie first (browser +
 * SSR document requests), then fall back to the `Authorization: Bearer` header
 * (integrations / direct-API clients). Composing both keeps bearer clients
 * working unchanged while cookie clients resolve with no JS-attached token.
 */
export function accessTokenExtractor(): JwtFromRequestFunction {
  return ExtractJwt.fromExtractors([
    cookieTokenExtractor(ACCESS_COOKIE),
    ExtractJwt.fromAuthHeaderAsBearerToken(),
  ]);
}

export function refreshTokenExtractor(): JwtFromRequestFunction {
  return ExtractJwt.fromExtractors([
    cookieTokenExtractor(REFRESH_COOKIE),
    ExtractJwt.fromAuthHeaderAsBearerToken(),
  ]);
}

/**
 * JWT Access Token Strategy
 * Validates JWT access tokens for protected routes.
 * Extracts the token from the access cookie or the Authorization header and
 * verifies it using the secret key.
 * Returns the user information contained in the token payload if valid.
 */
@Injectable()
export class AuthJwtATStrategy extends PassportStrategy(StrategyJwt, 'jwt-at') {
  constructor() {
    super({
      jwtFromRequest: accessTokenExtractor(),
      ignoreExpiration: false,
      secretOrKey: envs.JWT_AT_SECRET,
      passReqToCallback: false,
    });
  }

  /**
   * Validates the JWT payload and extracts user information.
   * @param payload - The decoded JWT payload containing user information
   * @returns An object representing the signed user if valid; otherwise throws an exception
   */
  validate(payload: IJwtPayloadUser | IJwtPayloadUserIntegration) {
    const { sub, ...rest } = payload;
    return { id: sub, ...rest } as ISignedUser | ISignedUserIntegration;
  }
}

/**
 * JWT Refresh Token Strategy
 * Validates JWT refresh tokens for token renewal.
 * Extracts the token from the Authorization header and verifies it using the secret key.
 * Returns the user information along with the refresh token if valid.
 */
@Injectable()
export class AuthJwtRTStrategy extends PassportStrategy(StrategyJwt, 'jwt-rt') {
  private readonly logger = new Logger(AuthJwtRTStrategy.name);
  constructor() {
    super({
      jwtFromRequest: refreshTokenExtractor(),
      ignoreExpiration: false,
      secretOrKey: envs.JWT_RT_SECRET,
      passReqToCallback: true,
    });
  }

  /**
   * Validates the JWT payload and extracts user information along with the refresh token.
   * @param req - The HTTP request object containing the Authorization header
   * @param payload - The decoded JWT payload containing user information
   * @returns An object representing the refresh user if valid; otherwise throws an exception
   */
  validate(
    req: Request,
    payload: IJwtPayloadUser | IJwtPayloadUserIntegration,
  ) {
    // Re-extract the raw RT the strategy authenticated with: bearer header for
    // integrations, refresh cookie for browser/SSR clients (U2 dual transport).
    const bearer = req.get('Authorization')?.replace('Bearer ', '').trim();
    const refreshToken = bearer || readCookie(req, REFRESH_COOKIE) || undefined;
    const { sub, ...rest } = payload;
    const refreshUser = { id: sub, ...rest, refreshToken } as IRefreshUser;
    // S6: never log the refresh token (a replayable 1d credential) — id only.
    this.logger.debug('jwt-rt strategy validate for user id:', sub);
    return refreshUser;
  }
}
