import { EmailTemplate } from '@hsm-lib/common/definitions/enums';

type EnumObject = Record<string, string>;

type EnumValues<T> = T extends EnumObject ? T[keyof T] : never;

type EmailTemplateEnumUnion =
  (typeof EmailTemplate)[keyof typeof EmailTemplate];

export type EmailTemplateName = EnumValues<EmailTemplateEnumUnion>;
