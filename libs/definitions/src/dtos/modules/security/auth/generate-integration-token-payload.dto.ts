import { FunctionalityRole } from '@hsm-lib/definitions/enums';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

export class CreateUserIntegrationDto {
  @IsString()
  @IsNotEmpty()
  name: string;

  @IsString()
  @IsNotEmpty()
  description: string;

  @IsEnum(FunctionalityRole)
  @IsNotEmpty()
  functionality: FunctionalityRole;
}
