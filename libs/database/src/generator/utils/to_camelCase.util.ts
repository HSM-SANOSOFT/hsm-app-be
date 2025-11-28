import { normalize } from '@hsm-lib/database/generator/utils/normalize.util';

export function toCamelCase(name: string): string;
export function toCamelCase(name: null | undefined): undefined;
export function toCamelCase(name: string | null | undefined) {
  const value = normalize(name);
  if (value === undefined) return undefined;

  return value.toLowerCase().replace(/_([a-z0-9])/g, (_, c) => c.toUpperCase());
}
