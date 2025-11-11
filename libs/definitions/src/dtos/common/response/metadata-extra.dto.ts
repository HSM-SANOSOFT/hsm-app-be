import { FilterDto, PaginationDto, SortDto } from '@hsm-lib/definitions/dtos';
import { ApiPropertyOptional } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { IsArray, IsOptional, ValidateNested } from 'class-validator';

export class MetadataExtraDto {
  @ApiPropertyOptional({
    description: 'Pagination information',
    type: () => PaginationDto,
  })
  @IsOptional()
  @ValidateNested()
  @Type(() => PaginationDto)
  pagination?: PaginationDto;

  @ApiPropertyOptional({
    description: 'Sorting information for the response data',
    type: () => SortDto,
  })
  @IsOptional()
  @ValidateNested()
  @Type(() => SortDto)
  sort?: SortDto;

  @ApiPropertyOptional({
    description: 'Filters applied to the data query',
    type: () => FilterDto,
    isArray: true,
  })
  @IsOptional()
  @IsArray()
  @ValidateNested({ each: true })
  @Type(() => FilterDto)
  filters?: FilterDto[];
}
