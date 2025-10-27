import type * as RoleEnums from '@hsm-lib/definitions/enums';

export type Roles =
  | RoleEnums.ClinicalRoles
  | RoleEnums.SystemRoles
  | RoleEnums.AdministrativeRole
  | RoleEnums.DefaultRole;
