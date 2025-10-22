import { CanActivate, ExecutionContext, Injectable } from '@nestjs/common';
import { Reflector } from '@nestjs/core';

import { IUser } from '@hsm-lib/definitions/interfaces';

import { ROLES_KEY } from './roles.decorator';

import type { Roles } from '@hsm-lib/definitions/types/modules/security/roles';

@Injectable()
export class RolesGuard implements CanActivate {
  constructor(private reflector: Reflector) {}

  canActivate(context: ExecutionContext): boolean {
    const requiredRoles = this.reflector.getAllAndOverride<Roles[]>(ROLES_KEY, [
      context.getHandler(),
      context.getClass(),
    ]);

    console.debug('Required Roles:', requiredRoles);
    if (!requiredRoles) {
      return true;
    }
    const { user }: { user: IUser } = context.switchToHttp().getRequest();
    console.debug('User', user);
    return requiredRoles.some(role => user.roles?.includes(role));
  }
}
