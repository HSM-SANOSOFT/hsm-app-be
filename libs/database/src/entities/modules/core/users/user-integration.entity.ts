import { FunctionalityRole } from '@hsm-lib/common/enums';
import { RefreshTokenUserIntegrationEntity } from '@hsm-lib/database/entities/modules/security/auth';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  OneToMany,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'users_integration', schema: databaseSchemas.USERS })
export class UserIntegrationEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @Column({ type: 'citext' })
  name!: string;

  @Column({ type: 'citext' })
  description!: string;

  @OneToMany(
    () => RefreshTokenUserIntegrationEntity,
    refreshToken => refreshToken.user,
  )
  refreshToken!: RefreshTokenUserIntegrationEntity[];

  @Column({ type: 'enum', enum: FunctionalityRole })
  functionality!: FunctionalityRole;

  @Column({ type: 'boolean', default: true })
  isActive!: boolean;

  @CreateDateColumn()
  createdAt!: Date;

  @UpdateDateColumn()
  updatedAt!: Date;

  @DeleteDateColumn({ nullable: true })
  deletedAt?: Date;
}
