import { IsNotEmpty, IsString } from 'class-validator';

export class RecetaMedicaTemplateDto {
  @IsString()
  @IsNotEmpty()
  patientName: string;
}
