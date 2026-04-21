import { databaseSchemas } from '@hsm-lib/database/sources/database-schema.enum';
import { Column, Entity, JoinColumn, OneToOne, PrimaryColumn } from 'typeorm';
import { TemplatesEntity } from './templates.entity';

@Entity({ name: 'template_docs', schema: databaseSchemas.TEMPLATES })
export class TemplateDocEntity {
  @PrimaryColumn({ type: 'uuid' })
  id: string;

  @Column()
  category: string;

  @Column({ name: 'document_code' })
  documentCode: string;

  @Column()
  format: string;

  @Column()
  size: string;

  @Column()
  orientation: string;

  @OneToOne(
    () => TemplatesEntity,
    t => t.doc,
  )
  @JoinColumn({ name: 'id' })
  template: TemplatesEntity;
}
