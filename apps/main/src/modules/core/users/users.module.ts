import { Module } from '@nestjs/common';
import { RolesModule } from '../../security/roles/roles.module';
import { UsersService } from './users.service';

@Module({
  imports: [RolesModule],
  providers: [UsersService],
  exports: [UsersService],
})
export class UsersModule {}
