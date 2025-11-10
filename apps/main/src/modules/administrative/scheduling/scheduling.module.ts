import { Module } from '@nestjs/common';
import { AvailabilityModule } from './availability/availability.module';
import { ExceptionsModule } from './exceptions/exceptions.module';
import { ReservationsModule } from './reservations/reservations.module';
import { ResourcesModule } from './resources/resources.module';
import { ServicesModule } from './services/services.module';
import { SlotsModule } from './slots/slots.module';
import { TemplatesModule } from './templates/templates.module';

@Module({
  controllers: [],
  providers: [],
  imports: [
    ServicesModule,
    ResourcesModule,
    TemplatesModule,
    ExceptionsModule,
    SlotsModule,
    AvailabilityModule,
    ReservationsModule,
  ],
})
export class SchedulingModule {}
