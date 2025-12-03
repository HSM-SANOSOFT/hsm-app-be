import { Module } from '@nestjs/common';
import { AvailabilityModule } from './availability/availability.module';

@Module({
  controllers: [],
  providers: [],
  imports: [
    AvailabilityModule,
  ],
})
export class SchedulingModule {}
