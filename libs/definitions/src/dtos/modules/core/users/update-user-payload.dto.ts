import type { Roles } from '@hsm-lib/definitions/types';
import { ApiProperty } from '@nestjs/swagger';
import { IsNotEmpty, IsOptional, IsString } from 'class-validator';

export class UpdateUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  id!: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  username?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  email?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  password?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  firstName?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  secondName?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  firstLastName?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  secondLastName?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  phoneNumber?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  gender?: string;

  @IsOptional()
  @IsString()
  @ApiProperty({ required: false })
  role?: Roles;
}
