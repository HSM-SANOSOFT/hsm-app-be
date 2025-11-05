import { UserIntegrationEntity } from '@hsm-lib/database/entities';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  Index,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'refresh_token_user_integration', schema: 'auth' })
export class RefreshTokenUserIntegrationEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @ManyToOne(
    () => UserIntegrationEntity,
    user => user.refreshToken,
    { onDelete: 'CASCADE' },
  )
  @JoinColumn()
  user!: UserIntegrationEntity;

  @Column({ type: 'varchar' })
  @Index({ unique: true, where: '"deletedAt" IS NULL' })
  refreshToken!: string;

  @Column({ type: 'boolean', default: true })
  isActive!: boolean;

  @CreateDateColumn()
  createdAt!: Date;

  @UpdateDateColumn()
  updatedAt!: Date;

  @DeleteDateColumn({ nullable: true })
  deletedAt?: Date;
}
