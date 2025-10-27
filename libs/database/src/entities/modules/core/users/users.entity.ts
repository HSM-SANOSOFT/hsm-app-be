import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  Index,
  OneToMany,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

import { UserRoleEntity } from './user-roles.entity';

@Entity({ name: 'users', schema: 'users' })
export class UserEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @Column({ type: 'citext' })
  @Index({ unique: true, where: '"deleted_at" IS NULL' })
  username!: string;

  @Column({ type: 'citext' })
  @Index({ unique: true, where: '"deleted_at" IS NULL' })
  email!: string;

  @Column({ type: 'text' })
  password!: string;

  @Column({ name: 'first_name', type: 'varchar', nullable: false })
  firstName!: string;

  @Column({ name: 'second_name', type: 'varchar', nullable: true })
  secondName?: string;

  @Column({ name: 'first_last_name', type: 'varchar', nullable: false })
  firstLastName!: string;

  @Column({ name: 'second_last_name', type: 'varchar', nullable: true })
  secondLastName?: string;

  @Column({ name: 'phone_number', type: 'varchar', nullable: true })
  phoneNumber?: string;

  @Column({ type: 'varchar', nullable: true })
  gender?: string;

  @OneToMany(
    () => UserRoleEntity,
    userRoles => userRoles.user,
    {
      cascade: ['insert', 'update'],
    },
  )
  roles!: UserRoleEntity[];

  @Column({ name: 'last_login_at', type: 'timestamptz', nullable: true })
  lastLoginAt?: Date;

  @Column({ name: 'is_active', type: 'boolean', default: true })
  isActive!: boolean;

  @Column({ name: 'email_verified', type: 'boolean', default: false })
  emailVerified!: boolean;

  @Column({ name: 'phone_verified', type: 'boolean', default: false })
  phoneVerified!: boolean;

  @CreateDateColumn({ name: 'created_at', type: 'timestamptz' })
  createdAt!: Date;

  @UpdateDateColumn({ name: 'updated_at', type: 'timestamptz' })
  updatedAt!: Date;

  @DeleteDateColumn({ name: 'deleted_at', type: 'timestamptz', nullable: true })
  deletedAt?: Date;
}
