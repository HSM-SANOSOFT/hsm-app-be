import { TransferState } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type AppConfig, CONFIG_STATE_KEY } from './config.schema';
import { ConfigService } from './config.service';
import { provideTestConfig, TEST_API_HOST } from './config-testing';

/**
 * ConfigService sources its value from transfer state (U10): the SSR server
 * seeds `CONFIG_STATE_KEY` from `process.env`, the browser reads it back at
 * hydration — no `config.json` fetch. `set()` (the test/server seam) wins.
 */
describe('ConfigService', () => {
  it('sources config from transfer state with no network fetch', () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    TestBed.configureTestingModule({});
    TestBed.inject(TransferState).set(CONFIG_STATE_KEY, {
      apiBaseUrl: 'http://api.example',
    });

    const config = TestBed.inject(ConfigService);

    expect(config.apiBaseUrl).toBe('http://api.example');
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('fails loudly when the transferred config is invalid', () => {
    TestBed.configureTestingModule({});
    // Missing the required apiBaseUrl.
    TestBed.inject(TransferState).set(CONFIG_STATE_KEY, {} as AppConfig);

    const config = TestBed.inject(ConfigService);

    expect(() => config.apiBaseUrl).toThrowError(/Invalid runtime config/);
  });

  it('throws when no config was provided (no transfer state, no set)', () => {
    TestBed.configureTestingModule({});

    const config = TestBed.inject(ConfigService);

    expect(() => config.apiBaseUrl).toThrowError(/before config was provided/);
  });

  it('provideTestConfig seeds a base for specs', () => {
    TestBed.configureTestingModule({ providers: [provideTestConfig()] });

    const config = TestBed.inject(ConfigService);

    expect(config.apiBaseUrl).toBe(TEST_API_HOST);
  });
});
