import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres';
import {
  Check,
  Column,
  Entity,
  JoinColumn,
  ManyToOne,
  OneToOne,
  PrimaryGeneratedColumn,
} from 'typeorm';
import { TemplateComEmailEntity } from './template-com-email.entity';
import { TemplateComSmsEntity } from './template-com-sms.entity';
import { TemplateDocEntity } from './template-doc.entity';

@Check(
  `(type = 'base' AND base_template IS NULL) OR (type != 'base' AND base_template IS NOT NULL)`,
)
@Entity({ name: 'templates', schema: DatabasePostgresSchemasEnum.TEMPLATES })
export class TemplatesEntity {
  @PrimaryGeneratedColumn('uuid')
  id: string;

  @Column()
  type: string;

  @Column()
  name: string;

  @Column()
  isActive: boolean;

  @Column({ type: 'jsonb' })
  schema: object;

  @Column()
  content: string;

  @Column({ nullable: true })
  description: string;

  @Column({ name: 'base_template', nullable: true })
  baseTemplateId: string;

  @ManyToOne(() => TemplatesEntity, { nullable: true })
  @JoinColumn({ name: 'base_template' })
  baseTemplate: TemplatesEntity;

  @OneToOne(
    () => TemplateComEmailEntity,
    e => e.template,
  )
  comEmail: TemplateComEmailEntity;

  @OneToOne(
    () => TemplateComSmsEntity,
    e => e.template,
  )
  comSms: TemplateComSmsEntity;

  @OneToOne(
    () => TemplateDocEntity,
    e => e.template,
  )
  doc: TemplateDocEntity;
}
