import { Module } from '@nestjs/common';

import { ModulesRepositoryModule } from './modules/modules-repository.module';

@Module({
  imports: [ModulesRepositoryModule],
  controllers: [],
  providers: [],
  exports: [ModulesRepositoryModule],
})
export class DatabaseRepositoryModule {}
