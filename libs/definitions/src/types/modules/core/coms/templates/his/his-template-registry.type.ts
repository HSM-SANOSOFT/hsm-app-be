import { HisEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { HisTemplateDtoMap } from './his-template-dto.type';

export type HisEmailRegistry = {
  [K in HisEmailTemplate]: EmailTemplateSelector<HisTemplateDtoMap[K]>;
};
