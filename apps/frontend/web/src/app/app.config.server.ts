import { type ApplicationConfig, mergeApplicationConfig } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';

/**
 * Server-only providers merged over the shared `appConfig` (Track 2, U6).
 *
 * `provideServerRendering(withRoutes(serverRoutes))` stands up dynamic SSR. The
 * request-scoped cookie forwarding (U8) and transfer-state config (U10) add
 * their providers here on top of this base.
 */
const serverConfig: ApplicationConfig = {
  providers: [provideServerRendering(withRoutes(serverRoutes))],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
