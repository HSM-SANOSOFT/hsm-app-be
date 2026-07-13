import { type BaseEnvs, baseEnvs } from './base';
import { type FieldName, validateEnv } from './fields';

/** `@hsm/api` config: the shared base plus the API-only vars. */
export interface ApiEnvs extends BaseEnvs {
  APP_BASE_URL: string;
  SWAGGER_FAVICON: string;
  JWT_AT_SECRET: string;
  JWT_RT_SECRET: string;
  COOKIE_SECURE: boolean;
  COOKIE_DOMAIN?: string;
  DEFAULT_ADMIN_USERNAME?: string;
  DEFAULT_ADMIN_PASSWORD?: string;
}

interface ApiOnly {
  APP_BASE_URL: string;
  SWAGGER_FAVICON: string;
  JWT_AT_SECRET: string;
  JWT_RT_SECRET: string;
  COOKIE_SECURE: boolean;
  COOKIE_DOMAIN?: string;
  DEFAULT_ADMIN_USERNAME?: string;
  DEFAULT_ADMIN_PASSWORD?: string;
}

const API_KEYS: readonly FieldName[] = [
  'APP_BASE_URL',
  'SWAGGER_FAVICON',
  'JWT_AT_SECRET',
  'JWT_RT_SECRET',
  'COOKIE_SECURE',
  'COOKIE_DOMAIN',
  'DEFAULT_ADMIN_USERNAME',
  'DEFAULT_ADMIN_PASSWORD',
];

/**
 * Fail-closed guard (S3): session cookies must never ride cleartext HTTP in a
 * deployed environment. `COOKIE_SECURE` defaults to false for local dev/test
 * (plain http), so without this a prod/staging deploy that forgot to set
 * `COOKIE_SECURE=true` would silently emit the httpOnly session cookies over
 * http. Pure + exported so it is unit-testable without import-time gymnastics.
 */
export function assertCookieSecureForEnv(
  environment: string,
  cookieSecure: boolean,
): void {
  const requiresSecureCookies =
    environment === 'prod' || environment === 'staging';
  if (requiresSecureCookies && !cookieSecure) {
    throw new Error(
      `Config validation error: COOKIE_SECURE must be true when ENVIRONMENT is "${environment}" — refusing to emit session cookies over cleartext HTTP.`,
    );
  }
}

export const envs: Readonly<ApiEnvs> = Object.freeze({
  ...baseEnvs,
  ...validateEnv<ApiOnly>(API_KEYS),
});

// Boot-time invariant (S3): prod/staging must set COOKIE_SECURE=true.
assertCookieSecureForEnv(envs.ENVIRONMENT, envs.COOKIE_SECURE);

export type Envs = typeof envs;

export { getWebhookSigningKeys } from './base';
