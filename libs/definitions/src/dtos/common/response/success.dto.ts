import { MetadataDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { ValidateNested } from 'class-validator';

export class SuccessResponseDto<T> {
  @ApiProperty({
    description:
      'Metadata of the response (pagination, sorting, filters, etc.)',
    type: () => MetadataDto,
  })
  @ValidateNested()
  @Type(() => MetadataDto)
  metadata: MetadataDto;

  @ApiProperty({
    description: 'Payload of the response',
  })
  data!: T;
}
