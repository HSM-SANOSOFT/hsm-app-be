import { Role } from '@hsm-lib/definitions/enums';
import type { RoleDomains, Roles } from '@hsm-lib/definitions/types';
import { Injectable } from '@nestjs/common';
@Injectable()
export class RolesService {
  findRoleDomains(roles: Roles[]): { role: Roles; domain: RoleDomains }[] {
    const result: { role: Roles; domain: RoleDomains }[] = [];

    for (const role of roles) {
      for (const [domain, domainRoles] of Object.entries(Role)) {
        if (Object.values(domainRoles).includes(role)) {
          result.push({ role, domain: domain as RoleDomains });
          break;
        }
      }
    }

    return result;
  }
}
