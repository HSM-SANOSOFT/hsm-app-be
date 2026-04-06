import { FunctionalityRole, Role } from '@hsm-lib/common/enums';
import type { Roles } from '@hsm-lib/common/types';
import { ApiProperty, ApiSchema } from '@nestjs/swagger';
import {
  ArrayMinSize,
  IsArray,
  IsEnum,
  IsIn,
  IsNotEmpty,
  IsOptional,
  IsString,
} from 'class-validator';

@ApiSchema({ name: 'Create User Integration Payload' })
export class CreateUserIntegrationPayloadDto {
  @IsString()
  @IsNotEmpty()
  @ApiProperty({ required: true })
  name: string;

  @IsString()
  @IsNotEmpty()
  @ApiProperty({ required: true })
  description: string;

  @IsEnum(FunctionalityRole)
  @IsNotEmpty()
  @ApiProperty({ required: true, enum: FunctionalityRole })
  functionality: FunctionalityRole;
}
@ApiSchema({ name: 'Create User Payload' })
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

@ApiSchema({ name: 'Delete User Payload' })
export class DeleteUserPayloadDto {
  @IsNotEmpty()
  @IsString()
  @ApiProperty({ required: true })
  id!: string;
}

@ApiSchema({ name: 'Update User Payload' })
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
