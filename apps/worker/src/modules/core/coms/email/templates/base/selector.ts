import { BaseTemplateDto } from '@hsm-lib/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { BaseTemplate } from './base.template';

export type BaseTemplateDtoMap = {
  [BaseEmailTemplate.Base]: BaseTemplateDto;
};

export type BaseEmailRegistry = {
  [K in BaseEmailTemplate]: EmailTemplateSelector<BaseTemplateDtoMap[K]>;
};

export const BASE_EMAIL_REGISTRY: BaseEmailRegistry = {
  [BaseEmailTemplate.Base]: {
    template: BaseTemplate,
  },
} satisfies BaseEmailRegistry;
