import { MetadataDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { IsOptional, ValidateNested } from 'class-validator';
import { BaseResponseDto } from './base.dto';

export class SuccessResponseDto<T> extends BaseResponseDto {
  @ApiPropertyOptional({
    description:
      'Metadata of the response (pagination, sorting, filters, etc.)',
    type: () => MetadataDto,
  })
  @IsOptional()
  @ValidateNested()
  @Type(() => MetadataDto)
  metadata?: MetadataDto;

  @ApiProperty({
    description: 'Payload of the response',
    example: {},
  })
  data!: T;
}
