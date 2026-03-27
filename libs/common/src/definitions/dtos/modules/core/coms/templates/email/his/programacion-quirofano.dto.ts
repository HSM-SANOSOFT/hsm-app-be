import {
  IsArray,
  IsNotEmpty,
  IsNumber,
  IsOptional,
  IsString,
} from 'class-validator';

class CirugiaDto {
  @IsNotEmpty()
  @IsNumber()
  surgeryId: number;

  @IsNotEmpty()
  @IsString()
  surgeryType: string;

  @IsNotEmpty()
  @IsString()
  surgeryDate: string;

  @IsNotEmpty()
  @IsString()
  surgeryTime: string;

  @IsNotEmpty()
  @IsString()
  surgeonName: string;

  @IsNotEmpty()
  @IsNumber()
  operatingRoomId: number;

  @IsNotEmpty()
  @IsString()
  patientMedicalHistoryId: string;

  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsOptional()
  @IsString()
  medicalObservations?: string;
}

export class ProgramacionQuirofanoTemplateDto {
  @IsArray()
  @IsNotEmpty({ each: true })
  cirugias: CirugiaDto[];
}
