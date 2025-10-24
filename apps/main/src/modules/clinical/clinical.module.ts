import { Module } from '@nestjs/common';
import { PatientsModule } from './patients/patients.module';
import { AppointmentsModule } from './appointments/appointments.module';

@Module({
  imports: [PatientsModule, AppointmentsModule],
})
export class ClinicalModule {}
