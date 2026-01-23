import type { BaseTemplateDto } from '@hsm-lib/definitions/dtos';
import { BaseEmailTemplate } from '@hsm-lib/definitions/enums';
import type {
  EmailRegistry,
  EmailTemplateDtoType,
  EmailTemplateName,
} from '@hsm-lib/definitions/types';
import { Injectable, InternalServerErrorException } from '@nestjs/common';
import { renderToStaticMarkup } from 'react-dom/server';
import { emailRegistry } from '../email/templates';

@Injectable()
export class TemplateService {
  private getEmailEntry<T extends EmailTemplateName>(
    name: T,
  ): EmailRegistry[T] {
    const entry = emailRegistry[name];
    if (!entry) {
      throw new InternalServerErrorException(
        `Email template "${String(name)}" not found`,
      );
    }
    return entry;
  }

  getEmailTemplate<T extends EmailTemplateName>(
    name: T,
    data: unknown,
  ): { subject: string; html: string } {
    const bodyEntry = this.getEmailEntry(name);
    const baseEntry = this.getEmailEntry(BaseEmailTemplate.Base);

    const safedata = data as EmailTemplateDtoType<T>;
    const bodyNode = bodyEntry.template(safedata);
    if (bodyNode == null) {
      throw new InternalServerErrorException('Email body template error');
    }

    const fullNode = baseEntry.template({
      title: bodyEntry.title ?? 'Hospital Santamaria',
      body: bodyNode as React.ReactNode,
      currentYear: new Date().getFullYear(),
    } satisfies BaseTemplateDto);

    const html = renderToStaticMarkup(fullNode);

    return {
      subject: bodyEntry.subject ?? 'Hospital Santamaria',
      html: '<!doctype html>' + html,
    };
  }
}
