import {
  ArrayMinSize,
  IsArray,
  IsEnum,
  IsNotEmpty,
  IsString,
} from 'class-validator';

import { Role } from '@hsm-lib/definitions/enums';

import type { Roles } from '@hsm-lib/definitions/types';

export class CreateUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  username!: string;

  @IsNotEmpty()
  @IsString()
  email!: string;

  @IsNotEmpty()
  @IsString()
  password!: string;

  @IsNotEmpty()
  @IsString()
  firstName!: string;

  @IsString()
  secondName?: string;

  @IsNotEmpty()
  @IsString()
  firstLastName!: string;

  @IsString()
  secondLastName?: string;

  @IsString()
  phoneNumber?: string;

  @IsString()
  gender?: string;

  @ArrayMinSize(1)
  @IsArray()
  @IsEnum(Role, { each: true })
  role!: Roles[];
}
