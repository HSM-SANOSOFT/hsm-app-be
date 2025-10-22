import { SetMetadata } from '@nestjs/common';

import type { Roles as Role } from '@hsm-lib/definitions/types/modules/security/roles';

export const ROLES_KEY = 'roles';

export const Roles = (...roles: Role[]) => SetMetadata(ROLES_KEY, roles);
