import { UserEntity } from '@hsm-lib/database/entities';
import type { RoleDomains, Roles } from '@hsm-lib/definitions/types';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  Index,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
  Unique,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'user_roles', schema: 'users' })
@Unique(['userId', 'domain', 'role'])
export class UserRoleEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @ManyToOne(
    () => UserEntity,
    user => user.roles,
    { onDelete: 'CASCADE' },
  )
  @JoinColumn()
  user!: UserEntity;

  @Column({ type: 'varchar' })
  domain!: RoleDomains;

  @Column({ type: 'varchar' })
  @Index()
  role!: Roles;

  @CreateDateColumn()
  createdAt!: Date;

  @UpdateDateColumn()
  updatedAt!: Date;

  @DeleteDateColumn({ nullable: true })
  deletedAt?: Date;
}
