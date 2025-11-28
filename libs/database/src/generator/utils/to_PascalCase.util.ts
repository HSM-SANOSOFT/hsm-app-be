import { normalize } from '@hsm-lib/database/generator/utils/normalize.util';

export function toPascalCase(name: string): string;
export function toPascalCase(name: null | undefined): undefined;
export function toPascalCase(name: string | null | undefined) {
  const value = normalize(name);

  if (value === undefined) return undefined;

  return value
    .toLowerCase()
    .split('_')
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
}
