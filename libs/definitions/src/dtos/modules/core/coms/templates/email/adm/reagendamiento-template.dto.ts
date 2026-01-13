import { IsNotEmpty, IsString } from 'class-validator';

export class ReagendamientoTemplateDto {
  @IsNotEmpty()
  @IsString()
  patientName: string;

  @IsNotEmpty()
  @IsString()
  scheduledDate: string;

  @IsNotEmpty()
  @IsString()
  rescheduledDate: string;

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
}
