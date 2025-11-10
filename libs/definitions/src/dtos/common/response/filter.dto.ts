import { FilterEnum } from '@hsm-lib/definitions/enums';
import { ApiProperty } from '@nestjs/swagger';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

export class FilterDto {
  @ApiProperty({
    description: 'Field name on which the filter is applied',
    example: 'role',
  })
  @IsString()
  @IsNotEmpty()
  field!: string;

  @ApiProperty({
    description: 'Conditional operator for the filter',
    enum: FilterEnum,
    example: FilterEnum.EQUAL,
  })
  @IsString()
  @IsEnum(FilterEnum)
  conditional!: string;

  @ApiProperty({
    description: 'Value used in the filter condition',
    example: 'doctor',
  })
  @IsString()
  @IsNotEmpty()
  value!: string;
}
