import { Module } from '@nestjs/common';
import { SchedulingModule } from './scheduling/scheduling.module';

@Module({
  imports: [SchedulingModule],
})
export class AdministrativeModule {}
