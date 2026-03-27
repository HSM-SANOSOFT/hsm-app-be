import type { Roles as Role } from '@hsm-lib/definitions/types/modules/security/roles';
import { SetMetadata } from '@nestjs/common';

export const ROLES_KEY = 'roles';

export const Roles = (...roles: Role[]) => SetMetadata(ROLES_KEY, roles);
