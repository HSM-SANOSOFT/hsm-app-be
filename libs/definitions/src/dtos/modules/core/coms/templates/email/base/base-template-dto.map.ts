import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import { BaseTemplateDto } from './base-template.dto';

export const BaseTemplateDtoMap = {
  [BaseEmailTemplate.Base]: BaseTemplateDto,
} as const;
