import { ApiProperty } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { IsInt, IsNotEmpty, Min } from 'class-validator';

export class PaginationDto {
  @ApiProperty({ description: 'Page number', example: 1 })
  @IsInt()
  @Min(1)
  @IsNotEmpty()
  @Type(() => Number)
  page!: number;

  @ApiProperty({ description: 'Page size', example: 10 })
  @IsInt()
  @Min(1)
  @IsNotEmpty()
  @Type(() => Number)
  pageSize!: number;

  @ApiProperty({ description: 'Total items', example: 100 })
  @IsInt()
  @Min(0)
  @IsNotEmpty()
  @Type(() => Number)
  totalItems!: number;

  @ApiProperty({ description: 'Total pages', example: 10 })
  @IsInt()
  @Min(0)
  @IsNotEmpty()
  @Type(() => Number)
  totalPages!: number;
}
