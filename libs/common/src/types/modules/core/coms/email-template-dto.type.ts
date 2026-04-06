import { EmailTemplateDtoMap } from
'@hsm-lib/common/
import type { EmailTemplateName } from
'@hsm-lib/common/

export type EmailTemplateDtoMapType = typeof EmailTemplateDtoMap;
export type EmailTemplateDtoType<T extends EmailTemplateName> = InstanceType<
  EmailTemplateDtoMapType[T]
>;
