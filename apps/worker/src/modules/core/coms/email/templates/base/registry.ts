import { BaseTemplateDtoMap } from '@hsm-lib/common/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/common/definitions/enums';
import { EmailRegistryFromDtoMap } from '@hsm-lib/common/definitions/types';

import { BaseTemplate } from './base.template';

export const baseEmailRegistry = {
  [BaseEmailTemplate.Base]: {
    template: BaseTemplate,
  },
} satisfies EmailRegistryFromDtoMap<typeof BaseTemplateDtoMap>;
