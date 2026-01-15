import { ValidateEmailTemplateData } from '@hsm-lib/common/validators';
import { EmailTemplate, EmailTemplateFlat } from '@hsm-lib/definitions/enums';
import type { EmailTemplateName } from '@hsm-lib/definitions/types';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import {
  ArrayNotEmpty,
  IsArray,
  IsEmail,
  IsEnum,
  IsNotEmpty,
  IsObject,
  IsOptional,
  IsString,
} from 'class-validator';

@ApiSchema({ name: 'Send Email Payload' })
export class SendEmailPayloadDto {
  @IsOptional()
  @IsString()
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
  @IsEnum(EmailTemplateFlat)
  @ApiProperty({
    description: 'plantilla de correo electrónico a utilizar',
    enum: EmailTemplateFlat,
    required: true,
    example: EmailTemplate.Auth.PinRestablecerContrasena,
  })
  emailTemplate: EmailTemplateName;

  //@IsOptional()
  @IsObject()
  @ValidateEmailTemplateData()
  @ApiProperty({
    description: 'data para la plantilla (merge vars)',
    required: false,
    example: { userName: 'Raul', pin: '123456' },
  })
  data: unknown;

  @IsOptional()
  @ApiProperty({
    description: 'archivos adjuntos (multipart/form-data)',
    required: false,
    type: 'string',
    format: 'binary',
    isArray: true,
  })
  files?: unknown[];
}
