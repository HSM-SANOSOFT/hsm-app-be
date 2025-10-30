import { Role } from '@hsm-lib/definitions/enums';
import type { Roles } from '@hsm-lib/definitions/types';
import { ApiProperty } from '@nestjs/swagger';
import {
  ArrayMinSize,
  IsArray,
  IsIn,
  IsNotEmpty,
  IsOptional,
  IsString,
} from 'class-validator';

export class CreateUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  username!: string;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  email!: string;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  password!: string;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  firstName!: string;

  @IsOptional()
  @IsString()
  @ApiProperty()
  secondName?: string;

  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  firstLastName!: string;

  @IsOptional()
  @IsString()
  @ApiProperty()
  secondLastName?: string;

  @IsOptional()
  @IsString()
  @ApiProperty()
  phoneNumber?: string;

  @IsOptional()
  @IsString()
  @ApiProperty()
  gender?: string;

  @ArrayMinSize(1)
  @IsArray()
  @IsIn(Object.values(Role).flatMap(Object.values) as readonly string[], {
    each: true,
  })
  @ApiProperty({
    required: true,
    enum: [Object.values(Role).flatMap(Object.values)],
  })
  roles!: Roles[];
}
