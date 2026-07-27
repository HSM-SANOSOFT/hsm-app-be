import { RenderMode, type ServerRoute } from '@angular/ssr';

/**
 * Server routing (Track 2, U6).
 *
 * Dynamic SSR first (scope boundary): every route renders on the Node server per
 * request — no prerendering / static server routes yet (deferred follow-up). The
 * authenticated shell is rendered off the forwarded access cookie as a read-only
 * probe (U8, model a); prerendering an authenticated shell would be incorrect.
 */
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
