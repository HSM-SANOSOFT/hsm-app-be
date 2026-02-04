import { DocumentsGeneratedEntity } from '@hsm-lib/database/entities/modules/core/docs/document-generated.entity';
import { DocumentStorageObjectEntity } from '@hsm-lib/database/entities/modules/core/docs/document-storage-object.entity';
import { DocumentsEntity } from '@hsm-lib/database/entities/modules/core/docs/documents.entity';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  ManyToOne,
  OneToOne,
  PrimaryGeneratedColumn,
  Unique,
} from 'typeorm';

@Entity({ name: 'documents-version', schema: databaseSchemas.DOCS })
@Unique(['document', 'version'])
export class DocumentsVersionEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @Column()
  version: number;

  @Column({ nullable: true })
  filename?: string;

  @Column({ nullable: true })
  mimeType?: string;

  @Column({ nullable: true })
  size?: number;

  @ManyToOne(
    () => DocumentsEntity,
    document => document.versions,
  )
  @JoinColumn()
  document: DocumentsEntity;

  @OneToOne(() => DocumentStorageObjectEntity, { cascade: true })
  storage: DocumentStorageObjectEntity;

  @OneToOne(() => DocumentsGeneratedEntity, { cascade: true, nullable: true })
  generated?: DocumentsGeneratedEntity;

  @CreateDateColumn()
  createdAt: Date;
}
