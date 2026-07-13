// Writes the build-time UI version (git short SHA) into build-version.ts before
// `ng build` (U11, R13). Symmetric with the API's /v1/health/version. Run in CI
// / the image build stage; locally the committed `dev` default stands.
//
// Usage: node scripts/gen-version.mjs
//   Version source: WEB_APP_VERSION env, else `git rev-parse --short HEAD`,
//   else `dev`.
import { execSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

function resolveVersion() {
  if (process.env.WEB_APP_VERSION) {
    return process.env.WEB_APP_VERSION;
  }
  try {
    return execSync('git rev-parse --short HEAD').toString().trim();
  } catch {
    return 'dev';
  }
}

const version = resolveVersion();
const out = join(
  dirname(fileURLToPath(import.meta.url)),
  '../src/app/core/version/build-version.ts',
);

const contents = `/**
 * Build-time UI version (U11, R13). Symmetric with the API's
 * \`GET /v1/health/version\`: CI/the image build overwrites this with the git
 * short SHA via \`scripts/gen-version.mjs\` before \`ng build\`. Locally it stays
 * \`dev\`. Baked into the bundle — no runtime fetch, SSR-safe.
 */
export const BUILD_VERSION = '${version}';
`;

writeFileSync(out, contents);
// biome-ignore lint/suspicious/noConsole: build tool output
console.log(`[gen-version] wrote ${out} (BUILD_VERSION=${version})`);
