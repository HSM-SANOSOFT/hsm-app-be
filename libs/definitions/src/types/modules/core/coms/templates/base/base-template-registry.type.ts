import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import {
  BaseTemplateDtoMap,
  EmailTemplateSelector,
} from '@hsm-lib/definitions/types';

export type BaseEmailRegistry = {
  [K in BaseEmailTemplate]: EmailTemplateSelector<BaseTemplateDtoMap[K]>;
};
