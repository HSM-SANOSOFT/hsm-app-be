//import { ValidateEmailTemplateData } from '@hsm-lib/common/validators';

import { DocumentsPayloadDto } from '@hsm-lib/definitions/dtos/modules/core/docs/documents-payload.dto';
import {
  EMAIL_TEMPLATE_VALUES,
  EmailTemplate,
} from '@hsm-lib/definitions/enums';
import type { EmailTemplateName } from '@hsm-lib/definitions/types';
import { ApiProperty, ApiSchema, PartialType } from '@nestjs/swagger';
import {
  ArrayNotEmpty,
  IsArray,
  IsEmail,
  IsIn,
  IsNotEmpty,
  IsObject,
  IsOptional,
  IsString,
} from 'class-validator';

@ApiSchema({ name: 'Send Email Payload' })
export class SendEmailPayloadDto extends PartialType(DocumentsPayloadDto) {
  @IsOptional()
  @IsEmail()
  @ApiProperty({
    description: 'correo electrónico del remitente (from)',
    required: false,
    example: 'no-reply@hospitalsm.org',
  })
  fromEmail: string;

  @IsOptional()
  @IsString()
  @ApiProperty({
    description: 'nombre del remitente (from name)',
    required: false,
    example: 'Name Name',
  })
  fromName: string;

  @IsArray()
  @ArrayNotEmpty()
  @IsEmail({}, { each: true })
  @ApiProperty({
    description: 'correos electrónicos de los destinatarios',
    required: true,
    example: ['user1@example.com', 'user2@example.com'],
  })
  toEmails: string[];

  @IsNotEmpty()
  @IsIn(EMAIL_TEMPLATE_VALUES)
  @ApiProperty({
    description: 'plantilla de correo electrónico a utilizar',
    required: true,
    enum: EMAIL_TEMPLATE_VALUES,
    example: EmailTemplate.Auth.PinRestablecerContrasena,
  })
  emailTemplate: EmailTemplateName;

  @IsObject()
  //@ValidateEmailTemplateData()
  @ApiProperty({
    description: 'data para la plantilla (merge vars)',
    required: false,
    example: { userName: 'Raul', pin: '123456' },
  })
  data: unknown;
}
