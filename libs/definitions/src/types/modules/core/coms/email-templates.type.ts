import type * as EmailTemplates from '@hsm-lib/definitions/enums/modules/core/coms/email-templates.enum';

export type EmailTemplateType =
  | EmailTemplates.BaseEmailTemplate
  | EmailTemplates.AdmEmailTemplate
  | EmailTemplates.AuthEmailTemplate
  | EmailTemplates.FacEmailTemplate
  | EmailTemplates.HisEmailTemplate
  | EmailTemplates.MktEmailTemplate;
