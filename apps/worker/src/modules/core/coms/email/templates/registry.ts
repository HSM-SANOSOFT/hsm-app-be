import type { EmailRegistry } from '@hsm-lib/definitions/types';
import { admEmailRegistry } from './adm';
import { baseEmailRegistry } from './base';

export const emailRegistry: EmailRegistry = {
  ...admEmailRegistry,
  ...baseEmailRegistry,
};
