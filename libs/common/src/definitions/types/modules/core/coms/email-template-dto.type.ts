import { EmailTemplateDtoMap } from '@hsm-lib/common/definitions/dtos';
import type { EmailTemplateName } from '@hsm-lib/common/definitions/types';

export type EmailTemplateDtoMapType = typeof EmailTemplateDtoMap;
export type EmailTemplateDtoType<T extends EmailTemplateName> = InstanceType<
  EmailTemplateDtoMapType[T]
>;
