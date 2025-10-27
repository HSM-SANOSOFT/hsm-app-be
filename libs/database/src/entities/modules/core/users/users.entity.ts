import {
  RefreshTokenUserEntity,
  UserRoleEntity,
} from '@hsm-lib/database/entities';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  Index,
  OneToMany,
  OneToOne,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

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

  @Column({type: 'varchar', nullable: false })
  firstName!: string;

  @Column({type: 'varchar', nullable: true })
  secondName?: string;

  @Column({type: 'varchar', nullable: false })
  firstLastName!: string;

  @Column({ type: 'varchar', nullable: true })
  secondLastName?: string;

  @Column({ type: 'varchar', nullable: true })
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

  @OneToOne(
    () => RefreshTokenUserEntity,
    refreshToken => refreshToken.user,
  )
  refreshToken!: RefreshTokenUserEntity;

  @Column({ type: 'timestamptz', nullable: true })
  lastLoginAt?: Date;

  @Column({ type: 'boolean', default: true })
  isActive!: boolean;

  @Column({ type: 'boolean', default: false })
  emailVerified!: boolean;

  @Column({ type: 'boolean', default: false })
  phoneVerified!: boolean;

  @CreateDateColumn()
  createdAt!: Date;

  @UpdateDateColumn()
  updatedAt!: Date;

  @DeleteDateColumn({ nullable: true })
  deletedAt?: Date;
}
