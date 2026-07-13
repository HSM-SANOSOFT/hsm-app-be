import {
  type ApplicationConfig,
  inject,
  mergeApplicationConfig,
  provideAppInitializer,
  TransferState,
} from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { CONFIG_STATE_KEY, validateConfig } from './core/config/config.schema';
import { ConfigService } from './core/config/config.service';

/**
 * Reads the non-secret runtime config from the SSR server's `process.env`
 * (U10). Mirrors the retired `gen-config.mjs`: env change → restart → new
 * config, no rebuild. `apiBaseUrl` stays a full base (with `/v1`) here; U11
 * splits it host-only with per-endpoint versions.
 */
function readServerConfig() {
  return validateConfig({
    apiBaseUrl: process.env['WEB_API_BASE_URL'] ?? 'http://localhost:4201/v1',
    appVersion: process.env['WEB_APP_VERSION'] ?? 'dev',
    production:
      (process.env['WEB_PRODUCTION'] ?? 'false').toLowerCase() === 'true',
  });
}

/**
 * Server-only providers merged over the shared `appConfig` (Track 2, U6/U10).
 *
 * - `provideServerRendering(withRoutes(serverRoutes))` stands up dynamic SSR.
 * - An initializer seeds the runtime config from `process.env` into both
 *   `TransferState` (serialized to the browser, read back at hydration) and
 *   `ConfigService` (so server-side renders resolve it without a fetch).
 *
 * The request-scoped cookie forwarding (U8) adds its providers here too.
 */
const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    provideAppInitializer(() => {
      const config = readServerConfig();
      inject(TransferState).set(CONFIG_STATE_KEY, config);
      inject(ConfigService).set(config);
    }),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
