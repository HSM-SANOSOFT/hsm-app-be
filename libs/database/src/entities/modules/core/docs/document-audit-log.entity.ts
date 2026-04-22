import { DocumentsEntity } from '@hsm-lib/database/entities/modules/core/docs/documents.entity';
import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres/database-postgres.schemas';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({
  name: 'document-audit-log',
  schema: DatabasePostgresSchemasEnum.DOCS,
})
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
