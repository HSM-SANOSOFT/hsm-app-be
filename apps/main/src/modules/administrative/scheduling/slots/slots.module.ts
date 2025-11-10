import { Module } from '@nestjs/common';
import { SlotsController } from './slots.controller';

@Module({
  controllers: [SlotsController]
})
export class SlotsModule {}
