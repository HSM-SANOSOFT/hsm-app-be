import { IsNotEmpty, IsString } from 'class-validator';

export class ResultadoExamenesTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  labType: string;
}
