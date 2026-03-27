import type * as RoleEnums from '@hsm-lib/common/definitions/enums/modules/security/roles/roles.enum';

export type Roles =
  | RoleEnums.ClinicalRoles
  | RoleEnums.SystemRoles
  | RoleEnums.AdministrativeRole
  | RoleEnums.DefaultRole;
