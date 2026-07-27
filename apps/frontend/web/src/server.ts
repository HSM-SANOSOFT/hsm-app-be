import { join } from 'node:path';
import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';

/**
 * SSR Node server (Track 2, U6). Serves the browser bundle as static assets and
 * renders every other request through Angular. The web image runs this entry
 * instead of a static file server (U9); config comes from `process.env` via
 * transfer state (U10).
 */
const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

// Serve the built browser assets. `index: false` so the Angular engine renders
// index.html per request rather than express short-circuiting to a static shell.
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

// Render all other requests with Angular SSR.
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then(response =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

// Start the server when run directly (the web image entry, U9).
if (isMainModule(import.meta.url)) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, () => {
    // biome-ignore lint/suspicious/noConsole: server startup log
    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/** Request handler used by the Angular CLI (dev-server and build). */
export const reqHandler = createNodeRequestHandler(app);
