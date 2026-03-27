import type { DtoClass, EmailTemplateName } from '@hsm-lib/definitions/types';

import { AdmTemplateDtoMap } from './adm/adm-template-dto.map';
import { AuthTemplateDtoMap } from './auth/auth-template-dto.map';
import { BaseTemplateDtoMap } from './base/base-template-dto.map';
import { FacTemplateDtoMap } from './fac/fac-template-dto.map';
import { HisTemplateDtoMap } from './his/his-template-dto.map';
import { MktTemplateDtoMap } from './mkt/mkt-template-dto.map';

export const EmailTemplateDtoMap = {
  ...AdmTemplateDtoMap,
  ...AuthTemplateDtoMap,
  ...BaseTemplateDtoMap,
  ...FacTemplateDtoMap,
  ...HisTemplateDtoMap,
  ...MktTemplateDtoMap,
} as const satisfies Record<EmailTemplateName, DtoClass>;
