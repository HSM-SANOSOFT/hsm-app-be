import { IsNotEmpty, IsNumber, IsString } from 'class-validator';

export class PinInicioSesionTemplateDto {
  @IsNotEmpty()
  @IsString()
  date: string;

  @IsNotEmpty()
  @IsNumber()
  pin: number;
}
