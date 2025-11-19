import { EspecialidadEntity, PersonalEntity } from '@hsm-lib/database/entities';
import { Entity, Index, JoinColumn, ManyToOne, PrimaryColumn } from 'typeorm';

@Entity({
  name: 'ESPECIALIDADES_MEDICOS',
})
export class EspecialidadesMedicosEntity {
  @Index('ESPMDC_PRS_FK_I')
  @PrimaryColumn({
    name: 'PRS_CODIGO',
    length: 4,
    comment: 'Código del personal',
    primaryKeyConstraintName: 'ESPMDC_PK',
  })
  prsCodigo!: string;

  @Index('ESPMDC_ESP_FK_I')
  @PrimaryColumn({
    name: 'ESP_CODIGO',
    length: 3,
    comment: 'Código de especialidad',
    primaryKeyConstraintName: 'ESPMDC_PK',
  })
  espCodigo!: string;

  @ManyToOne(() => EspecialidadEntity)
  @JoinColumn({
    name: 'ESP_CODIGO',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'ESPMDC_ESP_FK',
  })
  especialidad!: EspecialidadEntity;

  @ManyToOne(() => PersonalEntity)
  @JoinColumn({
    name: 'PRS_CODIGO',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'ESPMDC_PRS_FK',
  })
  personal!: PersonalEntity;
}
