import { IsEnum, IsNotEmpty, IsOptional, IsString, Matches } from 'class-validator';

import { FunctionalityRole } from '@hsm-lib/definitions/enums/modules/security/roles';

export class GenerateIntegrationTokenDto {
  @IsString()
  @IsNotEmpty()
  name: string;

  @IsString()
  @IsNotEmpty()
  description: string;

  @IsEnum(FunctionalityRole)
  @IsNotEmpty()
  functionality: FunctionalityRole;

  @IsOptional()
  @Matches(/^\d+([smhdwy])$/, { message: 'expiresIn must be in the format of a number followed by s, m, h, d, w, or y' })
  expiresIn?: `${number}${'s' | 'm' | 'h' | 'd' | 'w' | 'y'}`;
}
