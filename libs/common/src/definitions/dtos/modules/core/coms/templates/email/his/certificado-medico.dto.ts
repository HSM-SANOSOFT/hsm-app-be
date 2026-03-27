import { IsNotEmpty, IsString } from 'class-validator';

export class CertificadoMedicoTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;
}
