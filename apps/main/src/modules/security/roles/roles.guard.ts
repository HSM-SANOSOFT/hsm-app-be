import {
  ISignedUser,
  ISignedUserIntegration,
} from '@hsm-lib/definitions/interfaces';
import type { Roles } from '@hsm-lib/definitions/types';
import {
  CanActivate,
  ExecutionContext,
  Injectable,
  InternalServerErrorException,
  Logger,
} from '@nestjs/common';
import { Reflector } from '@nestjs/core';
import { IS_PUBLIC_KEY } from '../auth/auth.decorator';
import { ROLES_KEY } from './roles.decorator';

@Injectable()
export class RolesGuard implements CanActivate {
  private readonly logger = new Logger(RolesGuard.name);
  constructor(private reflector: Reflector) {}

  canActivate(context: ExecutionContext): boolean {
    const requiredRoles = this.reflector.getAllAndOverride<Roles[]>(ROLES_KEY, [
      context.getHandler(),
      context.getClass(),
    ]);

    if (!requiredRoles) {
      return true;
    }

    const isPublic = this.reflector.getAllAndOverride<boolean>(IS_PUBLIC_KEY, [
      context.getHandler(),
      context.getClass(),
    ]);
    if (isPublic) {
      this.logger.error('Public route - RolesGuard should not be applied');
      throw new InternalServerErrorException({
        message: 'Public route - RolesGuard should not be applied',
      });
    }

    const { user }: { user: ISignedUser | ISignedUserIntegration } = context
      .switchToHttp()
      .getRequest();
    return requiredRoles.some(role =>
      user.roles.some(userRole => userRole === role),
    );
  }
}
