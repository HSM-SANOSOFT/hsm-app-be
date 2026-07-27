import { Injectable, inject, signal } from '@angular/core';

import { ApiClient } from '../api/api-client';
import { BUILD_VERSION } from './build-version';

/** Public version endpoint (relative to the `/v1` base URL). */
export const HEALTH_VERSION_PATH = '/health/version';

/** Shown in place of the API version when the call fails or hasn't resolved. */
export const API_VERSION_FALLBACK = 'unknown';

/**
 * Exposes the running UI + API versions for the login/shell footer.
 *
 * - {@link uiVersion} is a build-time value baked into the bundle
 *   ({@link BUILD_VERSION}), which CI replaces with the git short SHA. It never
 *   changes at runtime and needs no runtime fetch (SSR-safe).
 * - {@link apiVersion} is fetched from the public `GET /v1/health/version`
 *   endpoint (`@Public()`, no auth). On failure it falls back to a stable
 *   label rather than hanging on an indefinite spinner.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  private readonly api = inject(ApiClient);

  /** UI version, baked in at build time (CI sets the git short SHA). */
  readonly uiVersion: string = BUILD_VERSION;

  private readonly apiVersionSignal = signal<string | null>(null);

  /** The backend version, or `null` until {@link loadApiVersion} resolves. */
  readonly apiVersion = this.apiVersionSignal.asReadonly();

  /**
   * Fetches the API version once and stores it. On any error it sets a stable
   * fallback so the footer always resolves to a value (never a spinner).
   */
  loadApiVersion(): void {
    this.api.get<{ version: string }>(HEALTH_VERSION_PATH).subscribe({
      next: ({ version }) =>
        this.apiVersionSignal.set(version || API_VERSION_FALLBACK),
      error: () => this.apiVersionSignal.set(API_VERSION_FALLBACK),
    });
  }
}
