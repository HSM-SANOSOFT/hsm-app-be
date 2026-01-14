import type {
  EmailTemplateDtoMap,
  EmailTemplateName,
  EmailTemplateSelector,
} from '@hsm-lib/definitions/types';

export type EmailRegistry = {
  [K in EmailTemplateName]: EmailTemplateSelector<EmailTemplateDtoMap[K]>;
};
