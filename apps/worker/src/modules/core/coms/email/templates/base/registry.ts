import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import { BaseEmailRegistry } from '@hsm-lib/definitions/types';
import { BaseTemplate } from './base.template';

export const baseEmailRegistry: BaseEmailRegistry = {
  [BaseEmailTemplate.Base]: {
    template: BaseTemplate,
  },
} satisfies BaseEmailRegistry;
