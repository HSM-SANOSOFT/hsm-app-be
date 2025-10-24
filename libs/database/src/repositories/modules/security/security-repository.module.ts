import { Module } from '@nestjs/common';

import { AuthRepositoryModule } from './auth/auth-repository.module';

@Module({
  imports: [AuthRepositoryModule],
  controllers: [],
  providers: [],
  exports: [AuthRepositoryModule],
})
export class SecurityRepositoryModule {}
