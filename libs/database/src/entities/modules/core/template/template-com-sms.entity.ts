import { DatabasePostgresSchemasEnum } from '@hsm-lib/database/sources/postgres';
import { Column, Entity, JoinColumn, OneToOne, PrimaryColumn } from 'typeorm';
import { TemplatesEntity } from './templates.entity';
@Entity({
  name: 'template_coms_sms',
  schema: DatabasePostgresSchemasEnum.TEMPLATES,
})
export class TemplateComSmsEntity {
  @PrimaryColumn({ type: 'uuid' })
  id: string;

  @Column()
  method: string;

  @Column({ name: 'method_template_name' })
  methodTemplateName: string;

  @Column()
  from: string;

  @OneToOne(
    () => TemplatesEntity,
    t => t.comSms,
  )
  @JoinColumn({ name: 'id' })
  template: TemplatesEntity;
}
