import type {
  AdmTemplateDtoMap,
  AuthTemplateDtoMap,
  BaseTemplateDtoMap,
  EmailTemplateName,
  FacTemplateDtoMap,
  HisTemplateDtoMap,
  MktTemplateDtoMap,
} from '@hsm-lib/definitions/types';

export type EmailTemplateDtoMap = AdmTemplateDtoMap &
  BaseTemplateDtoMap &
  AuthTemplateDtoMap &
  FacTemplateDtoMap &
  HisTemplateDtoMap &
  MktTemplateDtoMap;
export type EmailTemplateDto<T extends EmailTemplateName> =
  EmailTemplateDtoMap[T];
