import { DocumentsVersionEntity } from '@hsm-lib/database/entities/modules/core/docs/document-version.entity';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  OneToOne,
  PrimaryColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({ name: 'document-storage-object', schema: databaseSchemas.DOCS })
export class DocumentStorageObjectEntity {
  @PrimaryColumn('uuid')
  id: string;

  @Column()
  path: string;

  @Column()
  bucket: string;

  @Column({ nullable: true })
  region?: string;

  @Column({ nullable: true })
  etag?: string;

  @CreateDateColumn()
  createdAt: Date;

  @UpdateDateColumn()
  updatedAt: Date;

  @OneToOne(() => DocumentsVersionEntity)
  @JoinColumn()
  version: DocumentsVersionEntity;
}
