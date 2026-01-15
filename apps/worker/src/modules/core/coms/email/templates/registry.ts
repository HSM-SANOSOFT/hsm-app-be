import type { EmailRegistry } from '@hsm-lib/definitions/types';
import { admEmailRegistry } from './adm';
import { authEmailRegistry } from './auth';
import { baseEmailRegistry } from './base';
import { facEmailRegistry } from './fac';
import { hisEmailRegistry } from './his';
import { mktEmailRegistry } from './mkt';

export const emailRegistry: EmailRegistry = {
  ...admEmailRegistry,
  ...authEmailRegistry,
  ...baseEmailRegistry,
  ...facEmailRegistry,
  ...hisEmailRegistry,
  ...mktEmailRegistry,
};
