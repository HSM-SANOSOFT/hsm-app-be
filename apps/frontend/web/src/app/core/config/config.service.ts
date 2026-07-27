import { Injectable, inject, TransferState } from '@angular/core';
import {
  type AppConfig,
  CONFIG_STATE_KEY,
  validateConfig,
} from './config.schema';

/**
 * Holds the non-secret runtime config. Under SSR the config is seeded from the
 * server's `process.env` into transfer state (`app.config.server.ts`, U10) and
 * serialized to the browser, where it is read back during hydration — no boot
 * `config.json` fetch. In specs it is seeded directly via `provideTestConfig()`.
 *
 * The value is sourced lazily from transfer state on first read (order-
 * independent w.r.t. app initializers); an explicit `set()` takes precedence,
 * which is how the test seam and the server seeder inject a known value.
 */
@Injectable({ providedIn: 'root' })
export class ConfigService {
  private readonly transferState = inject(TransferState, { optional: true });
  private config: AppConfig | null = null;

  set(config: AppConfig): void {
    this.config = config;
  }

  private get value(): AppConfig {
    if (this.config) {
      return this.config;
    }
    if (this.transferState?.hasKey(CONFIG_STATE_KEY)) {
      this.config = validateConfig(
        this.transferState.get(CONFIG_STATE_KEY, null),
      );
      return this.config;
    }
    throw new Error('ConfigService read before config was provided');
  }

  get apiBaseUrl(): string {
    return this.value.apiBaseUrl;
  }
}
