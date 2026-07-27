/** Default API version segment. Endpoints may override it per call (KTD6). */
export const DEFAULT_API_VERSION = 'v1';

/**
 * Composes a full API URL from the host-only base, a version segment, and a
 * request path (U11). The runtime config carries only the host (e.g.
 * `http://localhost:4201`); each caller names its version so `/v1` and `/v2`
 * endpoints can coexist off one base rather than baking `/v1` into it.
 */
export function apiUrl(
  host: string,
  path: string,
  version: string = DEFAULT_API_VERSION,
): string {
  const base = host.replace(/\/+$/, '');
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return `${base}/${version}${normalizedPath}`;
}
