import { Module } from '@nestjs/common';
import { APP_GUARD } from '@nestjs/core';

import { AuthModule } from './auth/auth.module';
import { AuthJwtGuard } from './auth/guard';
import { RolesGuard } from './roles/roles.guard';
import { RolesModule } from './roles/roles.module';

@Module({
  imports: [AuthModule, RolesModule],
  providers: [
    {
      provide: APP_GUARD,
      useClass: AuthJwtGuard,
    },
    {
      provide: APP_GUARD,
      useClass: RolesGuard,
    },
  ],
})
export class SecurityModule {}
