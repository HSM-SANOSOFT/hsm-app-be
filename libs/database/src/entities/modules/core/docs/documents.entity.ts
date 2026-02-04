import { DocumentAuditLogEntity } from '@hsm-lib/database/entities/modules/core/docs/document-audit-log.entity';
import { DocumentLinkEntity } from '@hsm-lib/database/entities/modules/core/docs/document-link.entity';
import { DocumentsVersionEntity } from '@hsm-lib/database/entities/modules/core/docs/document-version.entity';
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

@Entity({ name: 'documents', schema: databaseSchemas.DOCS })
export class DocumentsEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @Column()
  title: string;

  @Column({ nullable: true })
  description?: string;

  @Column()
  type: string;

  @Column()
  status: string;

  @Column()
  source: string;

  @CreateDateColumn()
  createdAt: Date;

  @DeleteDateColumn({ nullable: true })
  deletedAt?: Date;

  @UpdateDateColumn()
  updatedAt: Date;

  @OneToMany(
    () => DocumentsVersionEntity,
    version => version.document,
    { cascade: true },
  )
  versions: DocumentsVersionEntity[];

  @OneToMany(
    () => DocumentLinkEntity,
    link => link.document,
    { cascade: true },
  )
  links: DocumentLinkEntity[];

  @OneToMany(
    () => DocumentAuditLogEntity,
    audit => audit.document,
    { cascade: true },
  )
  audits: DocumentAuditLogEntity[];
}
