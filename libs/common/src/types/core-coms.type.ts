import { EmailTemplateDtoMap } from '@hsm-lib/common/dtos';

import { EmailTemplate } from '@hsm-lib/common/enums';
import type { DtoClass } from '@hsm-lib/common/types';

type EnumObject = Record<string, string>;

type EnumValues<T> = T extends EnumObject ? T[keyof T] : never;

type EmailTemplateEnumUnion =
  (typeof EmailTemplate)[keyof typeof EmailTemplate];

export type EmailTemplateName = EnumValues<EmailTemplateEnumUnion>;

export type EmailTemplateDomain = keyof typeof EmailTemplate;

export type EmailTemplateDtoMapType = typeof EmailTemplateDtoMap;
export type EmailTemplateDtoType<T extends EmailTemplateName> = InstanceType<
  EmailTemplateDtoMapType[T]
>;

export type EmailTemplateSelector<TData> = {
  title?: string;
  subject?: string;
  template: (data: TData) => React.ReactNode;
};

export type EmailRegistry = {
  [K in EmailTemplateName]: EmailTemplateSelector<EmailTemplateDtoType<K>>;
};

export type EmailRegistryFromDtoMap<TMap extends Record<string, DtoClass>> = {
  [K in keyof TMap]: EmailTemplateSelector<InstanceType<TMap[K]>>;
};
