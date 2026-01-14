import { BaseTemplateDto } from '@hsm-lib/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';

export type BaseTemplateDtoMap = {
  [BaseEmailTemplate.Base]: BaseTemplateDto;
};
