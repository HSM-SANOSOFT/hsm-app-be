import { apiUrl, DEFAULT_API_VERSION } from './api-url';

describe('apiUrl', () => {
  it('composes host + default version + path', () => {
    expect(apiUrl('http://localhost:4201', '/auth/login')).toBe(
      'http://localhost:4201/v1/auth/login',
    );
    expect(DEFAULT_API_VERSION).toBe('v1');
  });

  it('honors an explicit version segment', () => {
    expect(apiUrl('http://localhost:4201', '/widgets', 'v2')).toBe(
      'http://localhost:4201/v2/widgets',
    );
  });

  it('normalizes a missing leading slash and a trailing slash on the host', () => {
    expect(apiUrl('http://localhost:4201/', 'health/version')).toBe(
      'http://localhost:4201/v1/health/version',
    );
  });
});
