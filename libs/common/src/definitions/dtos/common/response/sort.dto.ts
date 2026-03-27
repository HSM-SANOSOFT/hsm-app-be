import { SortEnum } from '@hsm-lib/definitions/enums';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

@ApiSchema({ name: 'Metadata.Extra.Sort' })
export class SortDto {
  @ApiProperty({
    description: 'Field by which the data is sorted',
    example: 'createdAt',
  })
  @IsString()
  @IsNotEmpty()
  field!: string;

  @ApiProperty({
    description: 'Sorting order',
    enum: SortEnum,
    example: 'desc',
  })
  @IsEnum(SortEnum)
  order!: SortEnum;
}
