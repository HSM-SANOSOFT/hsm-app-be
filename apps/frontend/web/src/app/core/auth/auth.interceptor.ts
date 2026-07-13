import { isPlatformServer } from '@angular/common';
import {
  HttpClient,
  type HttpErrorResponse,
  type HttpEvent,
  type HttpInterceptorFn,
} from '@angular/common/http';
import { Injectable, inject, PLATFORM_ID, REQUEST } from '@angular/core';
import { Router } from '@angular/router';
import {
  BehaviorSubject,
  catchError,
  filter,
  map,
  type Observable,
  of,
  switchMap,
  take,
  throwError,
} from 'rxjs';
import { apiUrl } from '../api/api-url';
import type { SuccessResponse, Tokens } from '../api/response';
import { ConfigService } from '../config/config.service';
import { AuthService } from './auth.service';
import { CSRF_HEADER, CsrfService } from './csrf.service';

/**
 * Module-scoped single-in-flight refresh coordination. The interceptor is a
 * function, so the "only the first 401 refreshes" state lives at module scope:
 * every concurrent 401 sees the same `isRefreshing` flag and waits on the same
 * subject, which emits `true` once the cookie refresh completes (or errors to
 * release the waiters on failure).
 */
let isRefreshing = false;
let refreshSubject = new BehaviorSubject<boolean>(false);

/** HTTP methods that mutate state and therefore require a CSRF token. */
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/** Endpoint suffixes that manage their own auth and must not loop on refresh. */
const LOGIN_SUFFIX = '/auth/login';
const REFRESH_SUFFIX = '/auth/refresh';

function isAuthEndpoint(url: string): boolean {
  return url.endsWith(LOGIN_SUFFIX) || url.endsWith(REFRESH_SUFFIX);
}

function isUnauthorized(error: unknown): error is HttpErrorResponse {
  return (error as HttpErrorResponse | null)?.status === 401;
}

/**
 * Performs the raw refresh GET using the httpOnly refresh COOKIE (no bearer):
 * the cookie rides via `withCredentials` and the API rotates + sets new cookies
 * on the response. Isolated so the interceptor never depends on `ApiClient`
 * (which would re-enter this interceptor); the `/auth/refresh` suffix is skipped
 * by `isAuthEndpoint`, so there is no recursion. Spyable in tests.
 */
@Injectable({ providedIn: 'root' })
export class AuthRefreshClient {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);
  private get url(): string {
    return apiUrl(this.config.apiBaseUrl, REFRESH_SUFFIX);
  }

  refresh(): Observable<Tokens> {
    return this.http
      .get<SuccessResponse<Tokens>>(this.url, { withCredentials: true })
      .pipe(map(body => body.data));
  }
}

/**
 * Cookie-based auth interceptor (U4):
 * - the session rides every request via `withCredentials` — no JS-attached
 *   bearer, no token read from storage;
 * - authenticated mutations carry the CSRF header, its token resolved from
 *   {@link CsrfService} (pre-session mutations like login/signup are anonymous,
 *   so they are exempt — matching the API's bearer/pre-session CSRF skip);
 * - a 401 runs a single in-flight cookie refresh and retries; concurrent 401s
 *   queue on the shared subject. A dead refresh clears state and routes to
 *   `/login`.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const refreshClient = inject(AuthRefreshClient);
  const csrf = inject(CsrfService);

  // SSR cookie forwarding (U8, model a). Under server render the browser cookie
  // jar / `withCredentials` do not exist, so the incoming request's `Cookie`
  // header is forwarded explicitly onto outbound API calls — letting the API's
  // cookie extractor authenticate the render off the ACCESS cookie as a
  // read-only probe. The `REQUEST` token is request-scoped (a fresh injector per
  // render via `BootstrapContext`), so one user's cookie can never bleed into
  // another concurrent render's outbound calls (S5).
  const isServer = isPlatformServer(inject(PLATFORM_ID));
  const request = inject(REQUEST, { optional: true });
  const forwardedCookie = isServer
    ? (request?.headers.get('cookie') ?? null)
    : null;

  // Auth issuance/refresh endpoints manage their own auth; just ride the cookie.
  if (isAuthEndpoint(req.url)) {
    return next(req.clone({ withCredentials: true }));
  }

  const deps: RefreshDeps = { auth, router, refreshClient, csrf };

  // A re-runnable dispatch: cookies always ride; authenticated mutations also
  // carry a freshly-resolved CSRF header. Under SSR the forwarded `Cookie`
  // header rides too. Re-run verbatim after a refresh.
  const dispatch = (): Observable<HttpEvent<unknown>> => {
    const needsCsrf =
      MUTATING_METHODS.has(req.method.toUpperCase()) && auth.isAuthenticated();
    const token$: Observable<string | null> = needsCsrf
      ? csrf.getToken()
      : of(null);
    return token$.pipe(
      switchMap(token => {
        const setHeaders: Record<string, string> = {};
        if (token) {
          setHeaders[CSRF_HEADER] = token;
        }
        if (forwardedCookie) {
          setHeaders['Cookie'] = forwardedCookie;
        }
        return next(req.clone({ withCredentials: true, setHeaders }));
      }),
    );
  };

  return dispatch().pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error)) {
        return throwError(() => error);
      }
      // Read-only probe (model a): SSR never rotates the RT. A 401 during server
      // render surfaces as anonymous — the client refreshes after hydration and
      // the neutral authenticated shell is rendered meanwhile.
      if (isServer) {
        return throwError(() => error);
      }
      return handle401(dispatch, deps);
    }),
  );
};

interface RefreshDeps {
  auth: AuthService;
  router: Router;
  refreshClient: AuthRefreshClient;
  csrf: CsrfService;
}

function handle401(
  retry: () => Observable<HttpEvent<unknown>>,
  deps: RefreshDeps,
): Observable<HttpEvent<unknown>> {
  // A concurrent 401 while a refresh is already running: wait for it to finish.
  if (isRefreshing) {
    return refreshSubject.pipe(
      filter(done => done),
      take(1),
      switchMap(() => retry()),
    );
  }

  // First 401: open the refresh cycle.
  isRefreshing = true;
  refreshSubject = new BehaviorSubject<boolean>(false);

  return deps.refreshClient.refresh().pipe(
    switchMap(() => {
      isRefreshing = false;
      refreshSubject.next(true);
      return retry();
    }),
    catchError((refreshError: unknown) => {
      isRefreshing = false;
      // Release queued waiters with an error, then clear session state.
      refreshSubject.error(refreshError);
      deps.csrf.clear();
      deps.auth.onSessionLost();
      void deps.router.navigate(['/login']);
      return throwError(() => refreshError);
    }),
  );
}
