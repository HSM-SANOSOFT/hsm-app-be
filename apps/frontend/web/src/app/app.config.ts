import { isPlatformBrowser, registerLocaleData } from '@angular/common';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
} from '@angular/common/http';
import localeEn from '@angular/common/locales/en';
import localeEsEc from '@angular/common/locales/es-EC';
import {
  type ApplicationConfig,
  DEFAULT_CURRENCY_CODE,
  inject,
  isDevMode,
  LOCALE_ID,
  PLATFORM_ID,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import {
  provideClientHydration,
  withIncrementalHydration,
} from '@angular/platform-browser';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
import { PrimeNG, providePrimeNG } from 'primeng/config';
import { firstValueFrom } from 'rxjs';
import { HsmPreset } from '../theme/hsm-preset';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { LANG_STORAGE_KEY } from './core/i18n/language.service';
import { primeNgTranslationFor } from './core/i18n/primeng-translations';
import { TranslocoHttpLoader } from './core/i18n/transloco-loader';

// Register locale data for Angular's date/number/currency pipes (independent of
// Transloco's text i18n). LOCALE_ID below picks which one formats by default.
registerLocaleData(localeEsEc, 'es-EC');
registerLocaleData(localeEn, 'en');

/**
 * The persisted UI language at boot, normalized to an Angular LOCALE_ID.
 *
 * Runs at provider-construction time during BOTH the browser and the SSR
 * bootstrap, so the `localStorage` read is platform-guarded (R8): on the server
 * there is no persisted preference — fall back to the default locale. The
 * client re-applies the persisted language after hydration via `LanguageService`.
 */
function bootLocaleId(platformId: object): string {
  if (!isPlatformBrowser(platformId)) {
    return 'es-EC';
  }
  try {
    return localStorage.getItem(LANG_STORAGE_KEY) === 'en' ? 'en' : 'es-EC';
  } catch {
    return 'es-EC';
  }
}

/**
 * Root application providers.
 *
 * - Zoneless change detection (Angular 21 default, declared explicitly).
 * - `provideHttpClient(withFetch(), withInterceptors([authInterceptor]))` —
 *   the auth interceptor sends the session cookie (`withCredentials`), attaches
 *   the CSRF header on authenticated mutations, and performs the single-in-
 *   flight cookie refresh on 401 (KTD2).
 * - `provideAppInitializer` rehydrates the session on boot by probing the
 *   profile from the httpOnly session cookie before the first route resolves,
 *   so guards see the correct auth state.
 * - PrimeNG with the Aura theme preset.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Client hydration with incremental hydration — reuse the server-rendered
    // DOM instead of destroying+recreating it, and defer hydrating (and thus
    // loading JS for) below-the-fold blocks until they are needed (Track 2, U6).
    provideClientHydration(withIncrementalHydration()),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideTransloco({
      config: {
        availableLangs: ['es', 'en'],
        defaultLang: 'es',
        fallbackLang: 'es',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
        missingHandler: { logMissingKey: true, useFallbackTranslation: true },
      },
      loader: TranslocoHttpLoader,
    }),
    {
      provide: LOCALE_ID,
      useFactory: bootLocaleId,
      deps: [PLATFORM_ID],
    },
    { provide: DEFAULT_CURRENCY_CODE, useValue: 'USD' },
    // Runtime config is sourced from transfer state (U10): the SSR server reads
    // `process.env` and seeds it (`app.config.server.ts`), the browser reads it
    // back at hydration. `ConfigService` resolves it lazily — no boot fetch, no
    // config app-initializer here.
    //
    // Session restore probes the profile from the httpOnly cookie. Browser-only
    // here (R9, model a): SSR renders a neutral authenticated shell and the
    // client restores after hydration. U8 adds the server-side, request-scoped
    // forwarded-cookie probe as a read-only render input.
    provideAppInitializer(() => {
      const platformId = inject(PLATFORM_ID);
      const auth = inject(AuthService);
      if (!isPlatformBrowser(platformId)) {
        return;
      }
      return firstValueFrom(auth.restoreSession());
    }),
    provideAppInitializer(() => {
      // Re-apply PrimeNG chrome copy on every Transloco language change.
      const primeng = inject(PrimeNG);
      const transloco = inject(TranslocoService);
      transloco.langChanges$.subscribe(lang => {
        primeng.setTranslation(
          primeNgTranslationFor(lang === 'en' ? 'en' : 'es'),
        );
      });
    }),
    provideAnimationsAsync(),
    providePrimeNG({
      theme: {
        preset: HsmPreset,
        options: {
          // All-day light operations console; no dark-mode toggle.
          darkModeSelector: false,
          // Keep PrimeNG utilities below app styles so our chrome wins.
          cssLayer: { name: 'primeng', order: 'theme, base, primeng' },
        },
      },
    }),
    // PWA service worker — registered in production only (orthogonal to the
    // zoneless setup). Disabled in dev/test so the SW never intercepts the dev
    // server or TestBed. U15 owns the offline caching strategy + SwUpdate flow;
    // here it is asset-only via ngsw-config.json.
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
