import { EmailTemplateDtoMap } from '@hsm-lib/definitions/dtos';
import { BadRequestException, Injectable, Logger } from '@nestjs/common';
import { plainToInstance } from 'class-transformer';
import {
  registerDecorator,
  ValidationArguments,
  ValidationOptions,
  ValidatorConstraint,
  ValidatorConstraintInterface,
  validateSync,
} from 'class-validator';

type DtoClass = new (...args: unknown[]) => object;
type EmailTemplateKey = keyof typeof EmailTemplateDtoMap;

@ValidatorConstraint({ name: 'EmailTemplateData', async: false })
@Injectable()
export class EmailTemplateDataValidator
  implements ValidatorConstraintInterface
{
  private readonly logger = new Logger(EmailTemplateDataValidator.name);
  validate(value: unknown, args: ValidationArguments): boolean {
    const obj = args.object as { emailTemplate?: string };

    this.logger.debug(
      `validate() called. template = ${String(obj.emailTemplate)}`,
    );

    const template = obj.emailTemplate as EmailTemplateKey | undefined;

    this.logger.debug(`Template key: ${template}`);

    const hasKey = template
      ? Object.hasOwn(EmailTemplateDtoMap, template)
      : false;

    this.logger.debug(`map has key? ${hasKey}`);

    if (!template) return false;

    const Dto = EmailTemplateDtoMap[template] as unknown as DtoClass;

    const dtoName = Dto?.name ?? 'undefined';
    const dtoKeys = Dto ? Object.keys(new Dto()) : [];
    this.logger.debug(`Raw Dto: ${dtoName} , keys = ${dtoKeys.join(', ')}`);

    if (!Dto) return false;

    this.logger.debug(`Template: ${template}, DTO: ${Dto?.name}`);

    if (value == null || typeof value !== 'object') return false;

    const instance = plainToInstance(Dto, value);

    const errors = validateSync(instance, {
      whitelist: true,
      forbidNonWhitelisted: true,
      forbidUnknownValues: false,
    });

    if (errors.length) {
      const fieldErrors = errors.map(err => ({
        field: err.property,
        constraints: err.constraints ?? {},
      }));

      const messages = fieldErrors.flatMap(e => Object.values(e.constraints));
      this.logger.error(`Field Issues: ${messages.join('; ')}`);
      throw new BadRequestException(messages);
    }

    return errors.length === 0;
  }

  defaultMessage(args: ValidationArguments) {
    this.logger.debug(
      `defaultMessage() called, Validation arguments: ${JSON.stringify(args)}`,
    );
    return `data is invalid for email template`;
  }
}

export function ValidateEmailTemplateData(options?: ValidationOptions) {
  return (target: object, propertyName: string) => {
    registerDecorator({
      name: 'ValidateEmailTemplateData',
      target: target.constructor,
      propertyName,
      options,
      validator: EmailTemplateDataValidator,
    });
  };
}
