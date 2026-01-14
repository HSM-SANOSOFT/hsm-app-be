import { AdmEmailTemplate } from '@hsm-lib/definitions/enums';
import {
  AdmTemplateDtoMap,
  EmailTemplateSelector,
} from '@hsm-lib/definitions/types';

export type AdmEmailRegistry = {
  [K in AdmEmailTemplate]: EmailTemplateSelector<AdmTemplateDtoMap[K]>;
};
