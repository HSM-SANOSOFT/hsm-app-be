import { Module } from '@nestjs/common';

import { RolesGuard } from './roles.guard';

@Module({
  controllers: [],
  providers: [{
    provide: 'ROLES_GUARD',
    useClass: RolesGuard,
  }],
})
export class RolesModule {}
