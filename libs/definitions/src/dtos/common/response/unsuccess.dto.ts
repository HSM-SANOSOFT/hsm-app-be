import { ErrorDto } from '@hsm-lib/definitions/dtos';
import { ApiProperty } from '@nestjs/swagger';
import { Type } from 'class-transformer';
import { ValidateNested } from 'class-validator';
import { BaseResponseDto } from './base.dto';

export class UnsuccessResponseDto extends BaseResponseDto {
  @ApiProperty({
    description: 'Error details describing why the request failed',
    type: () => ErrorDto,
  })
  @ValidateNested()
  @Type(() => ErrorDto)
  error!: ErrorDto;
}
