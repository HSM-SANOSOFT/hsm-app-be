import {
  Column,
  Entity,
  Index,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
  Unique,
} from 'typeorm';

import { UserEntity } from './users.entity';

import type { RoleDomains, Roles } from '@hsm-lib/definitions/types';

@Entity({ name: 'user_roles', schema: 'users' })
@Unique(['userId', 'domain', 'role'])
export class UserRoleEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @Column({ name: 'user_id', type: 'uuid' })
  @Index()
  userId!: string;

  @ManyToOne(() => UserEntity, user => user.roles, { onDelete: 'CASCADE' })
  @JoinColumn({ name: 'user_id' })
  user!: UserEntity;

  @Column({ type: 'varchar' })
  domain!: RoleDomains;

  @Column({ type: 'varchar' })
  @Index()
  role!: Roles;
}
