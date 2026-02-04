import { DocumentsEntity } from '@hsm-lib/database/entities/modules/core/docs/documents.entity';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({ name: 'document-link', schema: databaseSchemas.DOCS })
export class DocumentLinkEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @ManyToOne(
    () => DocumentsEntity,
    document => document.links,
  )
  @JoinColumn()
  document: DocumentsEntity;

  @Column()
  entityId: string;

  @Column()
  entityType: string;
}
