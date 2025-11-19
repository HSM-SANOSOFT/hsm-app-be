import { AreasEntity } from '@hsm-lib/database/entities';
import {
  Column,
  Entity,
  Index,
  JoinColumn,
  ManyToOne,
  PrimaryColumn,
} from 'typeorm';

@Entity({
  name: 'DEPARTAMENTOS',
})
export class DepartamentosEntity {
  @Index('DPR_ARA_FK_I')
  @PrimaryColumn({
    name: 'ARA_CODIGO',
    length: 1,
    primaryKeyConstraintName: 'DPR_PK',
    comment: 'Código del área.',
  })
  araCodigo!: string;

  @PrimaryColumn({
    name: 'CODIGO',
    length: 1,
    primaryKeyConstraintName: 'DPR_PK',
    comment: 'Código del departamento',
  })
  codigo!: string;

  @Column({
    name: 'NOMBRE',
    length: 30,
    comment: 'Nombre del departamento',
  })
  nombre!: string;

  @Column({
    name: 'CARGABLE',
    length: 1,
    comment: 'Si el departamento puede generar cargos o no',
  })
  cargable!: string;

  @Column({
    name: 'ESTADO_DE_DISPONIBILIDAD',
    length: 1,
    comment: 'Si está disponible, fuera de servicio, etc',
  })
  estadoDeDisponibilidad!: string;

  @Column({
    name: 'BODEGA',
    length: 1,
    comment: 'Si el departamento es bodega (tiene inventario local)',
  })
  bodega!: string;

  @Column({
    name: 'DIAS_CALCULO_STOCK',
    type: 'number',
    nullable: true,
  })
  diasCalculoStock!: number | null;

  @Column({
    name: 'DIAS_STOCK_MINIMO',
    type: 'number',
    nullable: true,
  })
  diasStockMinimo!: number | null;

  @Column({
    name: 'DIAS_STOCK_MAXIMO',
    type: 'number',
    nullable: true,
  })
  diasStockMaximo!: number | null;

  @Column({
    name: 'PRD_CODIGO',
    type: 'number',
    nullable: true,
  })
  prdCodigo!: number | null;

  @Column({
    name: 'ORIGEN',
    length: 1,
    nullable: true,
  })
  origen!: string | null;

  @Column({
    name: 'CALCULAR_STOCK_TOTAL',
    length: 1,
    nullable: true,
  })
  calcularStockTotal!: string | null;

  @Column({
    name: 'MTV_CODIGO',
    length: 2,
    nullable: true,
  })
  mtvCodigo!: string | null;

  @Column({
    name: 'SOBRESTOCK',
    length: 2,
    comment: 'Indica si se puede solicitar insumos mayor al stock',
  })
  sobrestock!: string;

  @Column({
    name: 'NOM_COR',
    length: 10,
    comment: 'Nombre corto',
  })
  nomCor!: string;

  @Column({
    name: 'IMAGEN',
    length: 1,
    comment: 'V si pertenece al centro de diagnóstico (imagen)',
  })
  imagen!: string;

  @ManyToOne(() => AreasEntity)
  @JoinColumn({
    name: 'ARA_CODIGO',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'DPR_ARA_FK',
  })
  area!: AreasEntity;
}
