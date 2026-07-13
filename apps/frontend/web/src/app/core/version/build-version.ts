/**
 * Build-time UI version (U11, R13). Symmetric with the API's
 * `GET /v1/health/version`: CI/the image build overwrites this with the git
 * short SHA via `scripts/gen-version.mjs` before `ng build`. Locally it stays
 * `dev`. Baked into the bundle — no runtime fetch, SSR-safe.
 */
export const BUILD_VERSION = 'dev';
