import { IssueDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty, ApiPropertyOptional } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { ValidateNested } from 'class-validator';
import { MetadataWithoutExtra } from './metadata.dto';

export class UnsuccessResponseDto {
  @ApiPropertyOptional({
    description: 'Metadata of the unsuccess response',
    type: () => MetadataWithoutExtra,
  })
  @ValidateNested()
  @Type(() => MetadataWithoutExtra)
  metadata: MetadataWithoutExtra;

  @ApiProperty({
    description: 'Error details describing why the request failed',
    type: () => IssueDto,
  })
  @ValidateNested()
  @Type(() => IssueDto)
  issue!: IssueDto;
}
