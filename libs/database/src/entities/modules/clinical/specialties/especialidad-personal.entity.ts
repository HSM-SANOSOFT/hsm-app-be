import { Column, Entity, PrimaryColumn } from 'typeorm';

@Entity({
  name: 'ESPECIALIDAD_PERSONAL',
})
export class EspecialidadPersonalEntity {
  @PrimaryColumn({
    name: 'CODIGO',
    length: 3,
    comment: 'Código del cargo',
    primaryKeyConstraintName: 'ESPPRS_PK',
  })
  codigo: string;

  @Column({
    length: 30,
    comment: 'Especialidad del personal',
  })
  especialidad: string;

  @Column({
    length: 1,
    default: 'D',
    comment: 'Si esta disponible, fuera de servicio, etc',
  })
  estadoDeDisponibilidad: string;

  @Column({
    length: 3,
  })
  nivelConsulta: string;
}
