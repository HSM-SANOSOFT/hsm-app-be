import { FacEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { FacTemplateDtoMap } from './fac-template-dto.type';

export type FacEmailRegistry = {
  [K in FacEmailTemplate]: EmailTemplateSelector<FacTemplateDtoMap[K]>;
};
