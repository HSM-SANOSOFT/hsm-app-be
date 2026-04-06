import { BaseTemplateDtoMap } from '@hsm-lib/common/dtos';
import { BaseEmailTemplate } from '@hsm-lib/common/enums';
import { EmailRegistryFromDtoMap } from '@hsm-lib/common/types';

import { BaseTemplate } from './base.template';

export const baseEmailRegistry = {
  [BaseEmailTemplate.Base]: {
    template: BaseTemplate,
  },
} satisfies EmailRegistryFromDtoMap<typeof BaseTemplateDtoMap>;
