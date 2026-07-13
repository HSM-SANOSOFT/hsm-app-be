import type { Provider } from '@angular/core';
import type { AppConfig } from './config.schema';
import { ConfigService } from './config.service';

/** The host-only API base specs seed into `ConfigService` (U11). */
export const TEST_API_HOST = 'http://localhost:4201';

/**
 * The v1-composed API base specs assert request URLs against — i.e. what
 * `apiUrl(TEST_API_HOST, path)` produces for the default version. Kept stable
 * so existing `${TEST_API_BASE_URL}/path` expectations hold after the host-only
 * split (U11).
 */
export const TEST_API_BASE_URL = `${TEST_API_HOST}/v1`;

/**
 * Seeds `ConfigService` with a fixed host-only config for specs, so no config
 * fetch happens. Spread into a TestBed `providers` array.
 */
export function provideTestConfig(
  overrides: Partial<AppConfig> = {},
): Provider {
  const config: AppConfig = {
    apiBaseUrl: TEST_API_HOST,
    production: false,
    ...overrides,
  };
  return {
    provide: ConfigService,
    useFactory: () => {
      const service = new ConfigService();
      service.set(config);
      return service;
    },
  };
}
