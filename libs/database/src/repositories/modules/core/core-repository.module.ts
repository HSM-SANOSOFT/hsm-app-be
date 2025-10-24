import { Module } from '@nestjs/common';

import { UserRepositoryModule } from './users/user-repository.module';

@Module({
  imports: [UserRepositoryModule],
  controllers: [],
  providers: [],
  exports: [UserRepositoryModule],
})
export class CoreRepositoryModule {}
