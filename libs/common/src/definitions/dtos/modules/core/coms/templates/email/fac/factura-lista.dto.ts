import { IsNotEmpty, IsOptional, IsString } from 'class-validator';

export class FacturaListaTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsOptional()
  @IsString()
  invoiceNumber?: string;
}
