import type {
  DtoClass,
  EmailTemplateDtoType,
  EmailTemplateName,
  EmailTemplateSelector,
} from '@hsm-lib/definitions/types';

export type EmailRegistry = {
  [K in EmailTemplateName]: EmailTemplateSelector<EmailTemplateDtoType<K>>;
};

export type EmailRegistryFromDtoMap<TMap extends Record<string, DtoClass>> = {
  [K in keyof TMap]: EmailTemplateSelector<InstanceType<TMap[K]>>;
};
