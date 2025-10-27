import { UserEntity } from '@hsm-lib/database/entities';
import {
  Column,
  CreateDateColumn,
  DeleteDateColumn,
  Entity,
  Index,
  JoinColumn,
  OneToOne,
  PrimaryGeneratedColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'refresh_token_users', schema: 'auth' })
export class RefreshTokenUserEntity {
  @PrimaryGeneratedColumn('uuid')
  id!: string;

  @OneToOne(
    () => UserEntity,
    user => user.refreshToken,
    { onDelete: 'CASCADE' },
  )
  @JoinColumn()
  user!: UserEntity;

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
