import { IsNotEmpty, IsNumber, IsString } from 'class-validator';

export class PinResultadoExamenTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  date: string;

  @IsNotEmpty()
  @IsNumber()
  pin: number;
}
