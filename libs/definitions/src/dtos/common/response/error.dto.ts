import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';
import { IsArray, IsOptional, IsString, ValidateIf } from 'class-validator';

export class ErrorDto {
  @ApiProperty({
    description: 'Short, human-readable description of the error(s)',
    examples: [
      'Invalid credentials provided.',
      ['email must be an email', 'password must be at least 8 characters'],
    ],
    oneOf: [
      { type: 'string', example: 'Invalid credentials provided.' },
      {
        type: 'array',
        items: { type: 'string' },
        example: ['email must be an email', 'password too short'],
      },
    ],
  })
  @ValidateIf(o => typeof o.message === 'string')
  @IsString()
  @ValidateIf(o => Array.isArray(o.message))
  @IsArray()
  message!: string | string[];

  @ApiPropertyOptional({
    description: 'Optional machine-readable error code or identifier',
    example: 'AUTH_INVALID_CREDENTIALS',
  })
  @IsOptional()
  @IsString()
  code?: string;

  @ApiPropertyOptional({
    description:
      'Detailed technical message, stack trace, or cause (for debugging)',
    example: 'Password hash comparison failed.',
  })
  @IsOptional()
  @IsString()
  detail?: string;

  @ApiPropertyOptional({
    description: 'Field or parameter that caused the error (if applicable)',
    example: 'email',
  })
  @IsOptional()
  @IsString()
  field?: string;
}
