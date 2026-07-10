import type { ITokens } from '@hsm/common/interfaces';
import { envs } from '@hsm/config/api';
import type { CookieOptions, Request, Response } from 'express';

/**
 * Dual-transport auth cookies. The API keeps returning `{ access_token,
 * refresh_token }` in the body (integrations / direct-API clients are
 * unchanged); browser clients additionally receive these httpOnly cookies so
 * the session rides the document request (the SSR prerequisite). httpOnly +
 * SameSite=Strict are code constants; `secure`/`domain` come from env.
 */
export const ACCESS_COOKIE = 'access_token';
export const REFRESH_COOKIE = 'refresh_token';

/**
 * The refresh cookie is path-scoped so it is only sent to the auth routes that
 * need it (refresh + logout), never on every request. `/v1/auth` covers
 * `/v1/auth/refresh` and `/v1/auth/logout` under the API's URI versioning.
 */
export const REFRESH_COOKIE_PATH = '/v1/auth';

// Cookie lifetimes mirror the browser-user token expiries in AuthService
// (access 15m, refresh 1d). Integration tokens use different expiries but never
// use cookies, so the browser-user values are the correct maxAge here.
const ACCESS_COOKIE_MAX_AGE_MS = 15 * 60 * 1000;
const REFRESH_COOKIE_MAX_AGE_MS = 24 * 60 * 60 * 1000;

function baseCookieOptions(): CookieOptions {
  return {
    httpOnly: true,
    secure: envs.COOKIE_SECURE,
    sameSite: 'strict',
    ...(envs.COOKIE_DOMAIN ? { domain: envs.COOKIE_DOMAIN } : {}),
  };
}

/** Sets the httpOnly access + refresh cookies alongside the token body. */
export function setAuthCookies(res: Response, tokens: ITokens): void {
  res.cookie(ACCESS_COOKIE, tokens.access_token, {
    ...baseCookieOptions(),
    path: '/',
    maxAge: ACCESS_COOKIE_MAX_AGE_MS,
  });
  res.cookie(REFRESH_COOKIE, tokens.refresh_token, {
    ...baseCookieOptions(),
    path: REFRESH_COOKIE_PATH,
    maxAge: REFRESH_COOKIE_MAX_AGE_MS,
  });
}

/** Clears both auth cookies (must match the paths used when setting them). */
export function clearAuthCookies(res: Response): void {
  res.clearCookie(ACCESS_COOKIE, { ...baseCookieOptions(), path: '/' });
  res.clearCookie(REFRESH_COOKIE, {
    ...baseCookieOptions(),
    path: REFRESH_COOKIE_PATH,
  });
}

/** Reads a named cookie off the parsed request (requires cookie-parser). */
export function readCookie(req: Request, name: string): string | null {
  const cookies = (req as Request & { cookies?: Record<string, string> })
    .cookies;
  return cookies?.[name] ?? null;
}
