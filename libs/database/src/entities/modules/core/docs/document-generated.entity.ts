import { DocumentsVersionEntity } from '@hsm-lib/database/entities/modules/core/docs/document-version.entity';
import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import {
  Column,
  CreateDateColumn,
  Entity,
  JoinColumn,
  OneToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';

@Entity({ name: 'documents-generated', schema: databaseSchemas.DOCS })
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
