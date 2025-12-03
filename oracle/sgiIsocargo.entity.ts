import {
Entity,
Column,
ManyToOne,
OneToOne,
JoinColumn,
Index,
Unique,
} from 'typeorm';
  import { PersonalEntity } from './index';



@Entity({ name: 'SGI_ISOCARGO', schema: 'SIS' })
export class SgiIsocargoEntity {

  @Column({
  name: 'ISCAID',
    type: 'number',
    length: 22,
    precision: 4,
  })
  iscaid: number;

  @Column({
  name: 'ISCADETA',
    type: 'varchar',
    length: 100,
    nullable: true,
  })
  iscadeta: string | null;

  @Column({
  name: 'ISCARESPONSABE',
    type: 'varchar',
    length: 100,
    nullable: true,
  })
  iscaresponsabe: string | null;

  @Column({
  name: 'ISCACODIGO',
    type: 'varchar',
    length: 4,
    comment: "CODIGO PERSONAL RRESPONSABLE ACTUAL DEL CARGO",
    nullable: true,
  })
  iscacodigo: string | null;

  @Column({
  name: 'ISCARGO_PRS',
    type: 'varchar',
    length: 5,
    nullable: true,
  })
  iscargoPrs: string | null;


    @ManyToOne(
    () => PersonalEntity
    )
    @JoinColumn([
      { name: 'ISCACODIGO', referencedColumnName: 'codigo' }
    ])
    personal: PersonalEntity;


}
