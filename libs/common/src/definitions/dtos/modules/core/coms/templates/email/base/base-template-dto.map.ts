import { BaseEmailTemplate } from '@hsm-lib/common/definitions/enums';
import type { DtoClass } from '@hsm-lib/common/definitions/types';

import { BaseTemplateDto } from './base-template.dto';

export const BaseTemplateDtoMap = {
  [BaseEmailTemplate.Base]: BaseTemplateDto,
} as const satisfies Record<BaseEmailTemplate, DtoClass>;
