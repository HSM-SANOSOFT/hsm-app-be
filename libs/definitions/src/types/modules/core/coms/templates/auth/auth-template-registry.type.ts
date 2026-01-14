import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { AuthTemplateDtoMap } from './auth-template-dto.type';

export type AuthEmailRegistry = {
  [K in AuthEmailTemplate]: EmailTemplateSelector<AuthTemplateDtoMap[K]>;
};
