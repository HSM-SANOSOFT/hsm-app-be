import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { PLATFORM_ID, REQUEST } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import {
  provideTestConfig,
  TEST_API_BASE_URL,
} from '../../core/config/config-testing';
import type { SuccessResponse, Tokens } from '../api/response';
import { provideTranslocoTestingModule } from '../i18n/transloco-testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const base = TEST_API_BASE_URL;

function tokensBody(at: string, rt: string): SuccessResponse<Tokens> {
  return {
    data: { access_token: at, refresh_token: rt },
    metadata: {
      success: true,
      statusCode: 200,
      timestamp: '2026-06-24T00:00:00.000Z',
      path: '/v1/auth/refresh',
      message: 'OK',
    },
  };
}

function csrfBody(token: string): SuccessResponse<{ csrfToken: string }> {
  return {
    data: { csrfToken: token },
    metadata: {
      success: true,
      statusCode: 200,
      timestamp: '2026-06-24T00:00:00.000Z',
      path: '/v1/auth/csrf',
      message: 'OK',
    },
  };
}

const UNAUTHORIZED = {
  body: { metadata: {}, issue: {} },
  opts: { status: 401, statusText: 'Unauthorized' },
};

describe('authInterceptor (cookie transport + CSRF + single in-flight refresh)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let navigateSpy: ReturnType<typeof vi.fn>;
  let authStub: {
    isAuthenticated: ReturnType<typeof vi.fn>;
    onSessionLost: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    navigateSpy = vi.fn().mockResolvedValue(true);
    authStub = {
      isAuthenticated: vi.fn().mockReturnValue(true),
      onSessionLost: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        provideTestConfig(),
        ...provideTranslocoTestingModule(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate: navigateSpy } },
        { provide: AuthService, useValue: authStub },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sends requests with credentials and no JS-attached Authorization header', () => {
    http.get(`${base}/widgets`).subscribe();

    const req = httpMock.expectOne(`${base}/widgets`);
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(req.request.headers.has('x-csrf-token')).toBe(false);
    req.flush({});
  });

  it('rides the cookie on the login call without CSRF or refresh interference', () => {
    http.post(`${base}/auth/login`, {}).subscribe();

    const req = httpMock.expectOne(`${base}/auth/login`);
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.headers.has('x-csrf-token')).toBe(false);
    req.flush({});
  });

  it('attaches the CSRF header on an authenticated mutation (token from /auth/csrf)', () => {
    http.post(`${base}/widgets`, { name: 'x' }).subscribe();

    // The interceptor first resolves the CSRF token from the issuer endpoint.
    const csrfReq = httpMock.expectOne(`${base}/auth/csrf`);
    expect(csrfReq.request.withCredentials).toBe(true);
    csrfReq.flush(csrfBody('CSRF123'));

    const post = httpMock.expectOne(`${base}/widgets`);
    expect(post.request.headers.get('x-csrf-token')).toBe('CSRF123');
    expect(post.request.withCredentials).toBe(true);
    post.flush({ ok: true });
  });

  it('does not attach a CSRF header on an anonymous (pre-session) mutation', () => {
    authStub.isAuthenticated.mockReturnValue(false);
    http.post(`${base}/auth/signup`, {}).subscribe();

    const post = httpMock.expectOne(`${base}/auth/signup`);
    expect(post.request.headers.has('x-csrf-token')).toBe(false);
    post.flush({});
    httpMock.expectNone(`${base}/auth/csrf`);
  });

  it('refreshes once via the cookie (no bearer) on a 401 and retries the request', () => {
    let result: unknown;
    http.get(`${base}/widgets`).subscribe(r => {
      result = r;
    });

    const first = httpMock.expectOne(`${base}/widgets`);
    expect(first.request.withCredentials).toBe(true);
    first.flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);

    const refresh = httpMock.expectOne(`${base}/auth/refresh`);
    expect(refresh.request.method).toBe('GET');
    expect(refresh.request.withCredentials).toBe(true);
    expect(refresh.request.headers.has('Authorization')).toBe(false);
    refresh.flush(tokensBody('AT_NEW', 'RT_NEW'));

    const retry = httpMock.expectOne(`${base}/widgets`);
    expect(retry.request.withCredentials).toBe(true);
    retry.flush({ ok: true });

    expect(result).toEqual({ ok: true });
  });

  it('collapses concurrent 401s into a single cookie refresh and retries all', () => {
    const results: unknown[] = [];
    http.get(`${base}/a`).subscribe(r => results.push(r));
    http.get(`${base}/b`).subscribe(r => results.push(r));
    http.get(`${base}/c`).subscribe(r => results.push(r));

    httpMock.expectOne(`${base}/a`).flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);
    httpMock.expectOne(`${base}/b`).flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);
    httpMock.expectOne(`${base}/c`).flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);

    // Exactly one refresh (no stampede).
    httpMock
      .expectOne(`${base}/auth/refresh`)
      .flush(tokensBody('AT_NEW', 'RT_NEW'));

    httpMock.expectOne(`${base}/a`).flush({ id: 'a' });
    httpMock.expectOne(`${base}/b`).flush({ id: 'b' });
    httpMock.expectOne(`${base}/c`).flush({ id: 'c' });

    expect(results).toEqual([{ id: 'a' }, { id: 'b' }, { id: 'c' }]);
  });

  it('clears session and redirects to /login when the refresh fails', () => {
    let caught: unknown;
    http.get(`${base}/widgets`).subscribe({
      error: (err: unknown) => {
        caught = err;
      },
    });

    httpMock
      .expectOne(`${base}/widgets`)
      .flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);
    httpMock
      .expectOne(`${base}/auth/refresh`)
      .flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);

    expect(caught).toBeDefined();
    expect(authStub.onSessionLost).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/login']);
  });

  it('does not attach a Cookie header in the browser (no forwarded request)', () => {
    http.get(`${base}/widgets`).subscribe();

    const req = httpMock.expectOne(`${base}/widgets`);
    expect(req.request.headers.has('Cookie')).toBe(false);
    req.flush({});
  });
});

/**
 * SSR cookie forwarding (U8, model a). During server render the interceptor
 * forwards the incoming request's `Cookie` header onto outbound API calls so the
 * API authenticates the render off the ACCESS cookie, and NEVER rotates the RT
 * on a 401 (read-only probe). The forwarded cookie is bound to the request-
 * scoped `REQUEST` token, so concurrent renders can never cross cookies (S5).
 */
describe('authInterceptor SSR cookie forwarding (U8, read-only probe)', () => {
  function configureServer(cookie: string | null): {
    http: HttpClient;
    httpMock: HttpTestingController;
  } {
    TestBed.resetTestingModule();
    const request = new Request('http://ssr.internal/', {
      headers: cookie ? { cookie } : {},
    });
    TestBed.configureTestingModule({
      providers: [
        provideTestConfig(),
        ...provideTranslocoTestingModule(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        {
          provide: Router,
          useValue: { navigate: vi.fn().mockResolvedValue(true) },
        },
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: vi.fn().mockReturnValue(true),
            onSessionLost: vi.fn(),
          },
        },
        { provide: PLATFORM_ID, useValue: 'server' },
        { provide: REQUEST, useValue: request },
      ],
    });
    return {
      http: TestBed.inject(HttpClient),
      httpMock: TestBed.inject(HttpTestingController),
    };
  }

  it('forwards the incoming Cookie header on outbound API calls', () => {
    const { http, httpMock } = configureServer('hsm_at=AAA; hsm_rt=BBB');

    http.get(`${base}/auth/profile`).subscribe();

    const req = httpMock.expectOne(`${base}/auth/profile`);
    expect(req.request.headers.get('Cookie')).toBe('hsm_at=AAA; hsm_rt=BBB');
    expect(req.request.withCredentials).toBe(true);
    req.flush({});
    httpMock.verify();
  });

  it('never refreshes on a 401 — renders anonymous instead (no RT rotation)', () => {
    const { http, httpMock } = configureServer('hsm_at=EXPIRED');
    let caught: unknown;

    http.get(`${base}/auth/profile`).subscribe({
      error: (err: unknown) => {
        caught = err;
      },
    });

    httpMock
      .expectOne(`${base}/auth/profile`)
      .flush(UNAUTHORIZED.body, UNAUTHORIZED.opts);
    // Model (a): no server-side refresh round-trip.
    httpMock.expectNone(`${base}/auth/refresh`);
    expect(caught).toBeDefined();
    httpMock.verify();
  });

  it('binds the forwarded cookie per render — no cross-bleed (S5)', () => {
    const a = configureServer('session=USER_A');
    a.http.get(`${base}/auth/profile`).subscribe();
    const reqA = a.httpMock.expectOne(`${base}/auth/profile`);
    expect(reqA.request.headers.get('Cookie')).toBe('session=USER_A');
    reqA.flush({});
    a.httpMock.verify();

    const b = configureServer('session=USER_B');
    b.http.get(`${base}/auth/profile`).subscribe();
    const reqB = b.httpMock.expectOne(`${base}/auth/profile`);
    expect(reqB.request.headers.get('Cookie')).toBe('session=USER_B');
    reqB.flush({});
    b.httpMock.verify();
  });
});
