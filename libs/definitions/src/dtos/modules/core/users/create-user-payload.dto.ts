import { Role } from '@hsm-lib/definitions/enums';
import type { Roles } from '@hsm-lib/definitions/types';
import {
  ArrayMinSize,
  IsArray,
  IsIn,
  IsNotEmpty,
  IsString,
} from 'class-validator';

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
  @IsIn(Object.values(Role).flatMap(Object.values) as readonly string[], {
    each: true,
  })
  role!: Roles[];
}
