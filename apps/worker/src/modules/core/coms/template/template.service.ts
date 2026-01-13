import type { BaseTemplateDto } from '@hsm-lib/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import { Injectable, InternalServerErrorException } from '@nestjs/common';
import type { ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

import { ADM_EMAIL_REGISTRY } from '../email/templates/adm/selector';
import { BASE_EMAIL_REGISTRY } from '../email/templates/base/selector';

const EMAIL_REGISTRY = {
  ...ADM_EMAIL_REGISTRY,
  ...BASE_EMAIL_REGISTRY,
} as const;

type EmailRegistry = typeof EMAIL_REGISTRY;
type EmailTemplateName = keyof EmailRegistry;

type GetEmailTemplateDto<T extends EmailTemplateName> =
  EmailRegistry[T] extends { template: (data: infer D) => unknown } ? D : never;

@Injectable()
export class TemplateService {
  private getEntry<T extends EmailTemplateName>(name: T): EmailRegistry[T] {
    const entry = EMAIL_REGISTRY[name];
    if (!entry) {
      throw new InternalServerErrorException(
        `Email template "${String(name)}" not found`,
      );
    }
    return entry;
  }

  getEmailTemplate<T extends EmailTemplateName>(
    name: T,
    data: GetEmailTemplateDto<T>,
  ): { subject: string; html: string } {
    const bodyEntry = this.getEntry(name);
    const baseEntry = this.getEntry(BaseEmailTemplate.Base);

    const bodyNode = bodyEntry.template(data); // ✅ no intersection now
    if (bodyNode == null) {
      throw new InternalServerErrorException('Email body template error');
    }

    const fullNode = baseEntry.template({
      title: bodyEntry.subject ?? 'Hospital Santamaria',
      body: bodyNode as ReactNode,
      currentYear: new Date().getFullYear(),
    } satisfies BaseTemplateDto);

    return {
      subject: bodyEntry.subject ?? 'Hospital Santamaria',
      html: '<!doctype html>' + renderToStaticMarkup(fullNode),
    };
  }
}
