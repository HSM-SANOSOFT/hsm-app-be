import { FunctionalityRole } from '@hsm-lib/definitions/enums/modules/security/roles';
import { IsEnum, IsNotEmpty, IsString } from 'class-validator';

export class UserIntegrationDto {
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
