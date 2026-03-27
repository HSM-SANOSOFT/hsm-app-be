import { IsNotEmpty, IsNumber, IsString } from 'class-validator';

export class PinRestablecimientoContrasenaTemplateDto {
  @IsNotEmpty()
  @IsString()
  date: string;

  @IsNotEmpty()
  @IsNumber()
  pin: number;
}
