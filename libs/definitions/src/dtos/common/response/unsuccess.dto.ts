import { IssueDto, MetadataDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { ValidateNested } from 'class-validator';

export class UnsuccessResponseDto {
  @ApiPropertyOptional({
    description:
      'Metadata of the response (pagination, sorting, filters, etc.)',
    type: () => MetadataDto,
  })
  @ValidateNested()
  @Type(() => MetadataDto)
  metadata: MetadataDto;
  @ApiProperty({
    description: 'Error details describing why the request failed',
    type: () => IssueDto,
  })
  @ValidateNested()
  @Type(() => IssueDto)
  issue!: IssueDto;
}
