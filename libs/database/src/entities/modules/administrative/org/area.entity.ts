import { NomOrgCoordinacionEntity } from '@hsm-lib/database/entities';
import { Column, Entity, JoinColumn, ManyToOne, PrimaryColumn } from 'typeorm';

@Entity({
  name: 'AREAS',
})
export class AreasEntity {
  @PrimaryColumn({
    name: 'CODIGO',
    length: 1,
    primaryKeyConstraintName: 'ARA_PK',
    comment: 'Código del área.',
  })
  codigo!: string;

  @Column({
    name: 'NOMBRE',
    length: 30,
    comment: 'Nombre definido para el área',
  })
  nombre!: string;

  @Column({
    name: 'ESTADO_DE_DISPONIBILIDAD',
    length: 1,
    comment: 'Si está disponible, fuera de servicio, etc.',
  })
  estadoDeDisponibilidad!: string;

  @Column({
    name: 'COORDINACION',
    type: 'number',
    comment: 'Código de la tabla NOM_ORG_COORDINACION',
  })
  coordinacion!: number;

  @ManyToOne(() => NomOrgCoordinacionEntity)
  @JoinColumn({
    name: 'COORDINACION',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'ARA_FK',
  })
  nomOrgCoordinacion!: NomOrgCoordinacionEntity;
}
