import { HttpClient } from '@angular/common/http';
import { Injectable, inject, REQUEST } from '@angular/core';
import type { Translation, TranslocoLoader } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

/**
 * Loads a language's nested JSON from `public/i18n/<lang>.json` (served at the
 * site root as `/i18n/<lang>.json`). One HTTP fetch per language, cached by
 * Transloco after first load.
 *
 * Under SSR (R8) a bare `/i18n/<lang>.json` is not a valid request target on the
 * Node platform — `HttpClient` needs an absolute URL. The incoming request's
 * origin (via the `REQUEST` token) resolves it to an absolute URL against the
 * SSR server, which serves the same catalogs as static assets (U9). In the
 * browser `REQUEST` is null and the relative path is used unchanged.
 */
@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);
  private readonly request = inject(REQUEST, { optional: true });

  getTranslation(lang: string): Observable<Translation> {
    return this.http.get<Translation>(`${this.baseUrl()}/i18n/${lang}.json`);
  }

  /** Absolute server origin under SSR; empty (relative) in the browser. */
  private baseUrl(): string {
    const url = this.request?.url;
    if (!url) {
      return '';
    }
    try {
      return new URL(url).origin;
    } catch {
      return '';
    }
  }
}
