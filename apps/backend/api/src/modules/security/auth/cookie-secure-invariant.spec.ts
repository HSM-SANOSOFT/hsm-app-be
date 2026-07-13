import { assertCookieSecureForEnv } from '@hsm/config/api';

/**
 * S3: session cookies must never be emitted over cleartext HTTP in a deployed
 * environment. `COOKIE_SECURE` defaults to false for local dev/test, so the
 * boot invariant forces it true for prod/staging and fails boot otherwise.
 */
describe('assertCookieSecureForEnv (S3 — COOKIE_SECURE prod invariant)', () => {
  it.each(['prod', 'staging'])(
    'throws when %s runs with COOKIE_SECURE=false',
    environment => {
      expect(() => assertCookieSecureForEnv(environment, false)).toThrow(
        /COOKIE_SECURE must be true/,
      );
    },
  );

  it.each(['prod', 'staging'])(
    'passes when %s has COOKIE_SECURE=true',
    environment => {
      expect(() => assertCookieSecureForEnv(environment, true)).not.toThrow();
    },
  );

  it.each(['dev', 'test'])(
    'allows COOKIE_SECURE=false in local %s',
    environment => {
      expect(() => assertCookieSecureForEnv(environment, false)).not.toThrow();
    },
  );
});
