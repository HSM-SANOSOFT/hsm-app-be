import { FunctionalityRole } from '@hsm-lib/definitions/enums';
import { ApiProperty } from '@nestjs/swagger';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

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
