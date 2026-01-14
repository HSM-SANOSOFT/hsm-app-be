import { MktEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { MktTemplateDtoMap } from './mkt-template-dto.type';

export type MktEmailRegistry = {
  [K in MktEmailTemplate]: EmailTemplateSelector<MktTemplateDtoMap[K]>;
};
