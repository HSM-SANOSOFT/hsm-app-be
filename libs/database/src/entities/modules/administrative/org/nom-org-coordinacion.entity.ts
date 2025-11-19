import { NomOrgDireccionEntity } from '@hsm-lib/database/entities';
import {
  Column,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryColumn,
  Unique,
} from 'typeorm';

@Entity({
  name: 'NOM_ORG_COORDINACION',
})
@Unique('UK_COORDINA', ['direccion', 'coordinacion'])
export class NomOrgCoordinacionEntity {
  @PrimaryColumn({
    name: 'ID',
    type: 'number',
    primaryKeyConstraintName: 'PK_COORDINA',
  })
  id!: number;

  @Column({
    name: 'DIRECCION',
    type: 'number',
    nullable: true,
    comment: 'La direccion a la que pertenece',
  })
  direccion!: number | null;

  @Column({
    name: 'COORDINACION',
    type: 'number',
    comment: 'El código de la empresa de la que es el plan de cuentas',
  })
  coordinacion!: number;

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
    comment: 'Descripción del Cargo',
  })
  descripcionCargo!: string | null;

  @ManyToOne(() => NomOrgDireccionEntity)
  @JoinColumn({
    name: 'DIRECCION',
    referencedColumnName: 'id',
    foreignKeyConstraintName: 'FK_COORDINA',
  })
  direccionRef!: NomOrgDireccionEntity | null;
}
