import type { NextFunction, Request, Response } from 'express';
import { ACCESS_COOKIE } from '../auth/auth-cookie.util';
import {
  CSRF_COOKIE,
  CSRF_HEADER,
  doubleCsrfProtection,
  generateCsrfToken,
} from './csrf.util';

// A JWT-shaped access token whose payload decodes to { sub }. Only the payload
// segment is read for CSRF session binding; the signature is irrelevant here.
const accessTokenFor = (sub: string): string => {
  const header = Buffer.from(JSON.stringify({ alg: 'HS256' })).toString(
    'base64url',
  );
  const payload = Buffer.from(JSON.stringify({ sub })).toString('base64url');
  return `${header}.${payload}.sig`;
};

const AT = accessTokenFor('user-1');

type CapturingRes = Response & { __cookies: Record<string, string> };

const makeRes = (): CapturingRes => {
  const cookies: Record<string, string> = {};
  return {
    __cookies: cookies,
    cookie: jest.fn((name: string, value: string) => {
      cookies[name] = value;
    }),
  } as unknown as CapturingRes;
};

const makeReq = (opts: {
  method: string;
  cookies?: Record<string, string>;
  headers?: Record<string, string>;
}): Request =>
  ({
    method: opts.method,
    cookies: opts.cookies ?? {},
    headers: opts.headers ?? {},
  }) as unknown as Request;

/** Runs the middleware and returns the error passed to next() (or undefined). */
const runProtection = (req: Request): unknown => {
  let captured: unknown;
  const next: NextFunction = (err?: unknown) => {
    captured = err;
  };
  doubleCsrfProtection(req, {} as Response, next);
  return captured;
};

/** Mints a CSRF token + its cookie for the user-1 session. */
const issueToken = (): { token: string; csrfCookie: string } => {
  const res = makeRes();
  const token = generateCsrfToken(
    makeReq({ method: 'GET', cookies: { [ACCESS_COOKIE]: AT } }),
    res,
  );
  return { token, csrfCookie: res.__cookies[CSRF_COOKIE] };
};

describe('CSRF double-submit protection (U3)', () => {
  it('accepts a cookie-authenticated mutation with a matching cookie + header', () => {
    const { token, csrfCookie } = issueToken();
    const err = runProtection(
      makeReq({
        method: 'POST',
        cookies: { [ACCESS_COOKIE]: AT, [CSRF_COOKIE]: csrfCookie },
        headers: { [CSRF_HEADER]: token },
      }),
    );
    expect(err).toBeUndefined();
  });

  it('rejects a cookie-authenticated mutation with no CSRF header', () => {
    const { csrfCookie } = issueToken();
    const err = runProtection(
      makeReq({
        method: 'POST',
        cookies: { [ACCESS_COOKIE]: AT, [CSRF_COOKIE]: csrfCookie },
        headers: {},
      }),
    );
    expect(err).toBeDefined();
  });

  it('exempts bearer / integration mutations (no CSRF header required)', () => {
    const err = runProtection(
      makeReq({
        method: 'POST',
        headers: { authorization: 'Bearer integration-at' },
      }),
    );
    expect(err).toBeUndefined();
  });

  it('never blocks safe GET requests', () => {
    const err = runProtection(
      makeReq({ method: 'GET', cookies: { [ACCESS_COOKIE]: AT } }),
    );
    expect(err).toBeUndefined();
  });
});
