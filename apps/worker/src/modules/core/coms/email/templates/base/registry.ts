import { BaseTemplateDtoMap } from '@hsm-lib/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailRegistryFromDtoMap } from '@hsm-lib/definitions/types';

import { BaseTemplate } from './base.template';

export const baseEmailRegistry = {
  [BaseEmailTemplate.Base]: {
    template: BaseTemplate,
  },
} satisfies EmailRegistryFromDtoMap<typeof BaseTemplateDtoMap>;
