import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import type { DtoClass } from '@hsm-lib/definitions/types';

import { BaseTemplateDto } from './base-template.dto';

export const BaseTemplateDtoMap = {
  [BaseEmailTemplate.Base]: BaseTemplateDto,
} as const satisfies Record<BaseEmailTemplate, DtoClass>;
