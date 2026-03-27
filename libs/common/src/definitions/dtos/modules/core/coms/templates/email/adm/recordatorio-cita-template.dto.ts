import { IsNotEmpty, IsString } from 'class-validator';

export class RecordatorioCitaTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  appointmentDate: string;

  @IsNotEmpty()
  @IsString()
  appointmentTime: string;

  @IsNotEmpty()
  @IsString()
  appointmentDetails: string;

  @IsNotEmpty()
  @IsString()
  medicalSpecialty: string;

  @IsNotEmpty()
  @IsString()
  appointmentType: string;

  @IsNotEmpty()
  @IsString()
  appointmentNote: string;
}
