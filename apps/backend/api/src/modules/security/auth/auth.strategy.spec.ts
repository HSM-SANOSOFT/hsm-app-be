import { Logger } from '@nestjs/common';
import type { Request } from 'express';
import { ACCESS_COOKIE, REFRESH_COOKIE } from './auth-cookie.util';
import {
  AuthJwtRTStrategy,
  accessTokenExtractor,
  refreshTokenExtractor,
} from './auth.strategy';

const makeReq = (opts: {
  authorization?: string;
  cookies?: Record<string, string>;
}): Request =>
  ({
    headers: opts.authorization ? { authorization: opts.authorization } : {},
    get: (name: string) =>
      name.toLowerCase() === 'authorization' ? opts.authorization : undefined,
    cookies: opts.cookies,
  }) as unknown as Request;

// Both transports proven in one spec (U2): cookie-only and bearer-only requests
// each resolve, and a request with neither yields null.
describe('dual-transport JWT extractors (U2)', () => {
  describe('accessTokenExtractor', () => {
    const extract = accessTokenExtractor();

    it('reads the access cookie when there is no bearer header', () => {
      expect(
        extract(makeReq({ cookies: { [ACCESS_COOKIE]: 'cookie-at' } })),
      ).toBe('cookie-at');
    });

    it('reads the bearer header when there is no cookie (integration path)', () => {
      expect(extract(makeReq({ authorization: 'Bearer header-at' }))).toBe(
        'header-at',
      );
    });

    it('returns null when neither cookie nor bearer is present', () => {
      expect(extract(makeReq({}))).toBeNull();
    });
  });

  describe('refreshTokenExtractor', () => {
    const extract = refreshTokenExtractor();

    it('reads the refresh cookie when there is no bearer header', () => {
      expect(
        extract(makeReq({ cookies: { [REFRESH_COOKIE]: 'cookie-rt' } })),
      ).toBe('cookie-rt');
    });

    it('reads the bearer header when there is no cookie', () => {
      expect(extract(makeReq({ authorization: 'Bearer header-rt' }))).toBe(
        'header-rt',
      );
    });
  });
});

describe('AuthJwtRTStrategy.validate', () => {
  const payload = { sub: 'user-1', username: 'admin' } as never;

  it('re-extracts the RT from the bearer header (integration path)', () => {
    const user = new AuthJwtRTStrategy().validate(
      makeReq({ authorization: 'Bearer bearer-rt' }),
      payload,
    );
    expect(user).toMatchObject({ id: 'user-1', refreshToken: 'bearer-rt' });
  });

  it('falls back to the refresh cookie when no bearer header (U2)', () => {
    const user = new AuthJwtRTStrategy().validate(
      makeReq({ cookies: { [REFRESH_COOKIE]: 'cookie-rt' } }),
      payload,
    );
    expect(user).toMatchObject({ id: 'user-1', refreshToken: 'cookie-rt' });
  });

  it('never logs the refresh token (S6)', () => {
    const debugSpy = jest
      .spyOn(Logger.prototype, 'debug')
      .mockImplementation(() => undefined);
    new AuthJwtRTStrategy().validate(
      makeReq({ authorization: 'Bearer secret-rt' }),
      payload,
    );
    for (const call of debugSpy.mock.calls) {
      expect(JSON.stringify(call)).not.toContain('secret-rt');
    }
    debugSpy.mockRestore();
  });
});
