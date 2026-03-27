import { IssueDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty, ApiPropertyOptional, ApiSchema } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { ValidateNested } from 'class-validator';
import { MetadataDto } from './metadata.dto';

@ApiSchema({ name: 'Unsuccess Response' })
export class UnsuccessResponseDto {
  @ApiPropertyOptional({
    description: 'Metadata of the unsuccess response',
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
