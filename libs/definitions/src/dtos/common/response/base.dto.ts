import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';
import {
  IsBoolean,
  IsInt,
  IsISO8601,
  IsOptional,
  IsString,
  Max,
  Min,
} from 'class-validator';

export class BaseResponseDto {
  @ApiProperty({ description: 'Whether the request was successful' })
  @IsBoolean()
  success!: boolean;

  @ApiProperty({ description: 'HTTP status code', minimum: 100, maximum: 599 })
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
    example: 'Request processed successfully.',
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
}
