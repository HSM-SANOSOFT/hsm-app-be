import { DocumentsVersionEntity } from '@hsm-lib/database/entities/modules/core/docs/document-version.entity';
import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres/database-postgres.schemas';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  OneToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({
  name: 'documents-generated',
  schema: DatabasePostgresSchemasEnum.DOCS,
})
export class DocumentsGeneratedEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @Column()
  templateName: string;

  @Column({ type: 'jsonb' })
  data: object;

  @CreateDateColumn()
  createdAt: Date;

  @OneToOne(() => DocumentsVersionEntity)
  @JoinColumn()
  version: DocumentsVersionEntity;
}
