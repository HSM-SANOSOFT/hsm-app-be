import { Column, Entity, PrimaryColumn } from 'typeorm';

@Entity({
  name: 'NOM_ORG_DIRECCION',
})
export class NomOrgDireccionEntity {
  @PrimaryColumn({
    name: 'DIRECCION',
    type: 'number',
    primaryKeyConstraintName: 'PK_DIRECCION',
    comment: 'El código de la empresa de la que es el plan de cuentas',
  })
  direccion!: number;

  @Column({
    name: 'DESCRIPCION_PUESTO',
    length: 100,
    comment: 'Descripción del Cargo',
  })
  descripcionPuesto!: string;

  @Column({
    name: 'NOMCOR',
    length: 15,
    comment: 'Si esta disponible, fuera de servicio, etc',
  })
  nomcor!: string;

  @Column({
    name: 'ESTADO_DE_DISPONIBILIDAD',
    length: 1,
    comment: 'Si esta disponible, fuera de servicio, etc',
  })
  estadoDeDisponibilidad!: string;

  @Column({
    name: 'DESCRIPCION_CARGO',
    length: 100,
    nullable: true,
    comment: 'Descripción del puesto',
  })
  descripcionCargo!: string | null;
}
