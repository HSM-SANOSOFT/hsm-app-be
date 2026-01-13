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
}
