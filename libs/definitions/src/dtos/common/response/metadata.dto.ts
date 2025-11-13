import { MetadataExtraDto } from '@hsm-lib/definitions/dtos';
import {
  ApiProperty,
  ApiPropertyOptional,
  ApiSchema,
  OmitType,
} from '@nestjs/swagger';
import {
  IsBoolean,
  IsInt,
  IsISO8601,
  IsOptional,
  IsString,
  Max,
  Min,
} from 'class-validator';

@ApiSchema({ name: 'Metadata' })
export class MetadataDto {
  @ApiProperty({
    description: 'Whether the request was successful',
    examples: {
      success: true,
      unauthorized: false,
      forbidden: false,
      notFound: false,
      internalError: false,
    },
  })
  @IsBoolean()
  success!: boolean;

  @ApiProperty({
    description: 'HTTP status code',
    examples: {
      success: 200,
      unauthorized: 401,
      forbidden: 403,
      notFound: 404,
      internalError: 500,
    },
    minimum: 100,
    maximum: 599,
  })
  @IsInt()
  @Min(100)
  @Max(599)
  statusCode!: number;

  @ApiProperty({
    description: 'UTC timestamp (ISO 8601)',
    example: '2025-11-10T16:32:00.000Z',
  })
  @IsISO8601()
  timestamp!: string;

  @ApiProperty({
    description: 'Original request path',
    example: '/v1/example/resource',
  })
  @IsString()
  path!: string;

  @ApiProperty({
    description: 'Human-readable short message',
    examples: {
      success: 'Request processed successfully.',
      unauthorized: 'You are not authorized to perform this action.',
      forbidden: 'Access denied. You do not have sufficient permissions.',
      notFound: 'The requested resource was not found.',
      internalError: 'An unexpected server error occurred.',
    },
  })
  @IsString()
  message!: string;

  @ApiPropertyOptional({
    description: 'API version of the response contract',
    example: 'v1',
  })
  @IsOptional()
  @IsString()
  apiVersion?: string;

  @ApiPropertyOptional({
    description:
      'Extra metadata for the response like pagination, sorting, etc.',
    type: () => MetadataExtraDto,
  })
  extra?: MetadataExtraDto;
}

@ApiSchema({ name: 'Metadata without extra information' })
export class MetadataWithoutExtra extends OmitType(MetadataDto, [
  'extra',
] as const) {}
