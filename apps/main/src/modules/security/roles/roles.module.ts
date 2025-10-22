import { Module } from '@nestjs/common';

import { RolesGuard } from './roles.guard';

@Module({
  controllers: [],
  providers: [RolesGuard],
  exports: [RolesGuard],
})
export class RolesModule {}
