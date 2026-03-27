import { IsNotEmpty, IsString } from 'class-validator';

export class SolicitudExamenesTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  labType: string;
}
