import { IsNotEmpty, IsString } from 'class-validator';

export class DocumentoDerivacionAprobadoTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  medicalUnit: string;
}
