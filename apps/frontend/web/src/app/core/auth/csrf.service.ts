import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, shareReplay, tap } from 'rxjs';
import type { SuccessResponse } from '../api/response';
import { ConfigService } from '../config/config.service';

/** Path of the CSRF-token issuer endpoint (relative to the `/v1` base URL). */
export const CSRF_PATH = '/auth/csrf';

/** Header the browser echoes the double-submit token in (matches the API). */
export const CSRF_HEADER = 'x-csrf-token';

/**
 * Fetches and caches the signed double-submit CSRF token from
 * `GET /v1/auth/csrf`. The API's CSRF cookie is httpOnly, so JS cannot read the
 * token off the cookie — it is delivered by this endpoint instead and echoed in
 * the `x-csrf-token` header on mutations by the auth interceptor. Cached until
 * {@link clear} (called on logout / session loss) so it is fetched once per
 * session, not once per mutation.
 */
@Injectable({ providedIn: 'root' })
export class CsrfService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(ConfigService);

  private token$: Observable<string> | null = null;

  /** Returns the cached CSRF token, fetching (and caching) it on first use. */
  getToken(): Observable<string> {
    if (!this.token$) {
      this.token$ = this.http
        .get<SuccessResponse<{ csrfToken: string }>>(
          `${this.config.apiBaseUrl}${CSRF_PATH}`,
          { withCredentials: true },
        )
        .pipe(
          map(body => body.data.csrfToken),
          // Don't cache a failed fetch — let the next mutation retry.
          tap({ error: () => this.clear() }),
          shareReplay(1),
        );
    }
    return this.token$;
  }

  /** Drops the cached token so the next mutation re-fetches it. */
  clear(): void {
    this.token$ = null;
  }
}
