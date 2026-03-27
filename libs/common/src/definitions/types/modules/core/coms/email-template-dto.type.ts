import { EmailTemplateDtoMap } from '@hsm-lib/definitions/dtos';
import type { EmailTemplateName } from '@hsm-lib/definitions/types';

export type EmailTemplateDtoMapType = typeof EmailTemplateDtoMap;
export type EmailTemplateDtoType<T extends EmailTemplateName> = InstanceType<
  EmailTemplateDtoMapType[T]
>;
