import { DocumentsEntity } from '@hsm-lib/database/entities/modules/core/docs/documents.entity';
import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres/database-postgres.schemas';
import {
  Column,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({ name: 'document-link', schema: DatabasePostgresSchemasEnum.DOCS })
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
