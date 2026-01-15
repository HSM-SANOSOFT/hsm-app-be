import { IsNotEmpty, IsNumber, IsString } from 'class-validator';

export class AutoAdmisionTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  documentId: string;

  @IsNotEmpty()
  @IsString()
  date: string;

  @IsNotEmpty()
  @IsNumber()
  code: number;
}
