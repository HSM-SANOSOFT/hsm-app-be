import type * as EmailTemplates from '@hsm-lib/definitions/enums';

export type EmailTemplateName =
  | EmailTemplates.BaseEmailTemplate
  | EmailTemplates.AdmEmailTemplate
  | EmailTemplates.AuthEmailTemplate
  | EmailTemplates.FacEmailTemplate
  | EmailTemplates.HisEmailTemplate
  | EmailTemplates.MktEmailTemplate;
