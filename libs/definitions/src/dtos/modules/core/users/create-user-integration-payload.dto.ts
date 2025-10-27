import { IsEnum, IsNotEmpty, IsString } from 'class-validator';
import { FunctionalityRole } from '../../../../enums/modules/security/roles/roles.functionality.enum';

export class CreateUserIntegrationPayloadDto {
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
