import { TypeOrmModule } from '@nestjs/typeorm';

import { UserEntity, UserRoleEntity } from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';

export const UserRepository = TypeOrmModule.forFeature([UserEntity, UserRoleEntity], Databases.HsmDbPostgres);
