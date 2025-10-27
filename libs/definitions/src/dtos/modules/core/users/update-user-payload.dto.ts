import type { Roles } from '@hsm-lib/definitions/types';
import { IsNotEmpty, IsOptional, IsString } from 'class-validator';

export class UpdateUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  id!: string;

  @IsOptional()
  @IsString()
  username?: string;

  @IsOptional()
  @IsString()
  email?: string;

  @IsOptional()
  @IsString()
  password?: string;

  @IsOptional()
  @IsString()
  firstName?: string;

  @IsOptional()
  @IsString()
  secondName?: string;

  @IsOptional()
  @IsString()
  firstLastName?: string;

  @IsOptional()
  @IsString()
  secondLastName?: string;

  @IsOptional()
  @IsString()
  phoneNumber?: string;

  @IsOptional()
  @IsString()
  gender?: string;

  @IsOptional()
  @IsString()
  role?: Roles;
}
