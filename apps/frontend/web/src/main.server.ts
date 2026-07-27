import {
  type BootstrapContext,
  bootstrapApplication,
} from '@angular/platform-browser';
import { App } from './app/app';
import { config } from './app/app.config.server';

/**
 * Server bootstrap (Track 2, U6). The CLI/SSR engine calls this per request with
 * a fresh `BootstrapContext`, which is what makes SSR injectables (e.g. the
 * `REQUEST` token used for cookie forwarding in U8) request-scoped.
 */
const bootstrap = (context: BootstrapContext) =>
  bootstrapApplication(App, config, context);

export default bootstrap;
