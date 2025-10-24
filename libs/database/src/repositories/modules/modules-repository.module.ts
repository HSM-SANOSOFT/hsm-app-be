import { Module } from '@nestjs/common';

import { CoreRepositoryModule } from './core/core-repository.module';
import { SecurityRepositoryModule } from './security/security-repository.module';

@Module({
  imports: [CoreRepositoryModule, SecurityRepositoryModule],
  controllers: [],
  providers: [],
  exports: [CoreRepositoryModule, SecurityRepositoryModule],
})
export class ModulesRepositoryModule {}
