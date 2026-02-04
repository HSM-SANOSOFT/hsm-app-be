import { DocumentsEntity } from '@hsm-lib/database/entities/modules/core/docs/documents.entity';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({ name: 'document-audit-log', schema: databaseSchemas.DOCS })
export class DocumentAuditLogEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @ManyToOne(
    () => DocumentsEntity,
    document => document.audits,
  )
  @JoinColumn()
  document: DocumentsEntity;

  @Column()
  action: string;

  @CreateDateColumn()
  createdAt: Date;
}
