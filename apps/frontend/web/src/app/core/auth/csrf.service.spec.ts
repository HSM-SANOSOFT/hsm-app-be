import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  provideTestConfig,
  TEST_API_BASE_URL,
} from '../../core/config/config-testing';
import type { SuccessResponse } from '../api/response';
import { CsrfService } from './csrf.service';

const base = TEST_API_BASE_URL;

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

describe('CsrfService', () => {
  let csrf: CsrfService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    csrf = TestBed.inject(CsrfService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches the token from /auth/csrf with credentials and caches it', () => {
    const seen: string[] = [];
    csrf.getToken().subscribe(t => seen.push(t));

    const req = httpMock.expectOne(`${base}/auth/csrf`);
    expect(req.request.withCredentials).toBe(true);
    req.flush(csrfBody('C1'));

    // Second call is served from cache — no new HTTP request.
    csrf.getToken().subscribe(t => seen.push(t));
    httpMock.expectNone(`${base}/auth/csrf`);

    expect(seen).toEqual(['C1', 'C1']);
  });

  it('re-fetches after clear()', () => {
    csrf.getToken().subscribe();
    httpMock.expectOne(`${base}/auth/csrf`).flush(csrfBody('C1'));

    csrf.clear();

    let second: string | undefined;
    csrf.getToken().subscribe(t => {
      second = t;
    });
    httpMock.expectOne(`${base}/auth/csrf`).flush(csrfBody('C2'));
    expect(second).toBe('C2');
  });
});
