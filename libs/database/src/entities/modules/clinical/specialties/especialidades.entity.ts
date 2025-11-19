import { Column, Entity, PrimaryColumn } from 'typeorm';

@Entity({
  name: 'ESPECIALIDADES',
})
export class EspecialidadEntity {
  @PrimaryColumn({
    name: 'CODIGO',
    length: 3,
    primaryKeyConstraintName: 'ESP_PK',
    comment: 'Código de especialidad',
  })
  codigo: string;

  @Column({
    name: 'ESPECIALIDAD',
    length: 40,
    comment: 'Descripción de la Especialidad',
  })
  especialidad: string;

  @Column({
    name: 'ESTADO_DE_DISPONIBILIDAD',
    length: 1,
    default: 'D',
    comment: 'Estado de Disponibilidad',
  })
  estadoDeDisponibilidad: string;

  @Column({
    name: 'NIVEL_CONSULTA',
    length: 2,
    nullable: true,
    comment: 'CG-CODE Tipos de consultas que da el medico para precios',
  })
  nivelConsulta?: string | null;

  @Column({
    name: 'CODIGO_INEN',
    length: 2,
    nullable: true,
    comment: 'Codigo de la especialidad de egreso segun reporte inen',
  })
  codigoInen: string | null;

  @Column({
    name: 'AGRUPADOR',
    length: 2,
    nullable: true,
    default: null,
    comment:
      '1 Cirugia general 2 Ginecobstetrica 3 PEDIATRIA 4 MED.INTERNA 9  E ESPECIALIDADES 6 GASTRO 78NEURO 7 URO ',
  })
  agrupador: string | null;

  @Column({
    name: 'QUIROFANO',
    length: 2,
    nullable: true,
    default: 'Q1',
    comment: 'PARA CREAR PARTES',
  })
  quirofano: string | null;

  @Column({
    name: 'CRG_CODIGO_PC',
    length: 10,
    nullable: true,
    default: '99202',
    comment: 'PRIMERA CONSULTA PC',
  })
  crgCodigoPc: string | null;

  @Column({
    name: 'CRG_CODIGO_CS',
    length: 10,
    nullable: true,
    default: '99213',
    comment: 'CONSULTA SUBSECUENTE CS',
  })
  crgCodigoCs: string | null;
}
