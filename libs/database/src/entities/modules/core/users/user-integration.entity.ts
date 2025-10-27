import {
  RefreshTokenUserIntegrationEntity,
} from '@hsm-lib/database/entities';
import { FunctionalityRole } from '@hsm-lib/definitions/enums';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  OneToOne,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'users_integration', schema: 'users' })
export class UserIntegrationEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @Column({ type: 'citext' })
  name!: string;

  @Column({ type: 'citext' })
  description!: string;

  @OneToOne(
    () => RefreshTokenUserIntegrationEntity,
    refreshToken => refreshToken.user,
  )
  refreshToken!: RefreshTokenUserIntegrationEntity;
  
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
