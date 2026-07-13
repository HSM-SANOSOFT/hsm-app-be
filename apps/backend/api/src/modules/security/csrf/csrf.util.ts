import { envs } from '@hsm/config/api';
import { doubleCsrf } from 'csrf-csrf';
import type { Request } from 'express';
import { ACCESS_COOKIE, readCookie } from '../auth/auth-cookie.util';

/** Header the browser echoes the CSRF token in (double-submit). */
export const CSRF_HEADER = 'x-csrf-token';

/**
 * CSRF cookie name. Deliberately not `__Host-` prefixed: that prefix forbids a
 * `Domain` attribute, but this cookie is scoped to the shared parent domain
 * (COOKIE_DOMAIN) so the web subdomain and the API subdomain agree on it.
 */
export const CSRF_COOKIE = 'hsm.x-csrf-token';

/**
 * Binds the signed CSRF token to the authenticated user so a token minted for
 * one session cannot be replayed in another. `sub` is decoded (unverified) from
 * the access cookie — integrity comes from the HMAC secret (S1), so `sub` only
 * needs to be a stable per-user key, not a trusted claim. Returns '' when there
 * is no access cookie (those requests are never CSRF-protected; see
 * skipCsrfProtection), which keeps the identifier consistent across the
 * protected set.
 */
function sessionIdentifier(req: Request): string {
  const accessToken = readCookie(req, ACCESS_COOKIE);
  if (!accessToken) return '';
  try {
    const payloadSegment = accessToken.split('.')[1];
    if (!payloadSegment) return '';
    const claims = JSON.parse(
      Buffer.from(payloadSegment, 'base64url').toString('utf8'),
    ) as { sub?: string };
    return claims.sub ?? '';
  } catch {
    return '';
  }
}

const { doubleCsrfProtection, generateCsrfToken, invalidCsrfTokenError } =
  doubleCsrf({
    getSecret: () => envs.CSRF_SECRET,
    getSessionIdentifier: sessionIdentifier,
    cookieName: CSRF_COOKIE,
    cookieOptions: {
      sameSite: 'lax',
      secure: envs.COOKIE_SECURE,
      httpOnly: true,
      path: '/',
      ...(envs.COOKIE_DOMAIN ? { domain: envs.COOKIE_DOMAIN } : {}),
    },
    getCsrfTokenFromRequest: req =>
      req.headers[CSRF_HEADER] as string | undefined,
    // Protect only cookie-authenticated browser mutations. Bearer / integration
    // clients cannot be CSRF'd and must keep working untouched (R1); pre-session
    // requests (login / signup — no access cookie yet) carry no double-submit
    // token to check (login-CSRF is an accepted residual, mitigated by the
    // same-site Lax posture and fresh-session issuance).
    skipCsrfProtection: req =>
      Boolean(req.headers.authorization?.startsWith('Bearer ')) ||
      !readCookie(req, ACCESS_COOKIE),
  });

export { doubleCsrfProtection, generateCsrfToken, invalidCsrfTokenError };
