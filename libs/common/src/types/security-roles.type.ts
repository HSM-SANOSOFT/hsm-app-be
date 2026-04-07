import { Role } from '@hsm-lib/common/enums';
export type RoleDomains = keyof typeof Role;

type RoleValues<T> = T extends object ? T[keyof T] : never;

export type Roles = RoleValues<(typeof Role)[keyof typeof Role]>;
