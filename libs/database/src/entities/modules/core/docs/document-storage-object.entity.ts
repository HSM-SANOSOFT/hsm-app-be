import { DocumentsVersionEntity } from '@hsm-lib/database/entities/modules/core/docs/document-version.entity';
import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres/database-postgres.schemas';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  OneToOne,
  PrimaryColumn,
  UpdateDateColumn,
} from 'typeorm';

@Entity({
  name: 'document-storage-object',
  schema: DatabasePostgresSchemasEnum.DOCS,
})
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
