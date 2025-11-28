import {
Entity,
Column,
ManyToOne,
OneToOne,
JoinColumn,
Index,
Unique,
} from 'typeorm';
  import { SgiIsoorganigramaEntity, SgiIsoareasEntity, SsaClasificacionOcupacionesEntity, CompaniasEntity, SgiIsodptoEntity, SgiIsocargoEntity, SsaPuestosSutEntity, SgiIsodivisionEntity } from './index';

    @Unique('UK_CARGOS_SSA', [
      'COMPANIA', 
      'DIVISION', 
      'UNIDAD_DPTO', 
      'SERVICIO_AREA', 
      'ISCAID', 
      'CIOU', 
      'CARGOS_SUT', 
      'PR_SIGLAS', 
      'CARGO_ORG'
    ])


@Entity({ name: 'SSA_PUESTOS_TRABAJO', schema: 'SIS' })
export class SsaPuestosTrabajoEntity {

  @Column({
  name: 'NUMERO',
    type: 'number',
    length: 22,
    precision: 5,
  })
  numero: number;

  @Column({
  name: 'COMPANIA',
    type: 'varchar',
    length: 3,
    comment: "Identificador",
  })
  compania: string;

  @Column({
  name: 'CIOU',
    type: 'varchar',
    length: 20,
    comment: "Codigo de cargo",
  })
  ciou: string;

  @Column({
  name: 'PROCESO_SUT',
    type: 'varchar',
    length: 20,
    comment: "Segun sut",
  })
  procesoSut: string;

  @Column({
  name: 'PR_SIGLAS',
    type: 'varchar',
    length: 3,
    comment: "Segun sistema de gestion de calidad",
  })
  prSiglas: string;

  @Column({
  name: 'CARGOS_SUT',
    type: 'varchar',
    length: 2,
    comment: "Cargo segun sut",
  })
  cargosSut: string;

  @Column({
  name: 'DETALLE_ACTIVIDADES',
    type: 'varchar',
    length: 500,
    comment: "Del puesto",
  })
  detalleActividades: string;

  @Column({
  name: 'OTRO_NOMBRE_CARGO',
    type: 'varchar',
    length: 200,
  })
  otroNombreCargo: string;

  @Column({
  name: 'CARGO_ORG',
    type: 'varchar',
    length: 10,
    comment: "CARGO EN ORGANIGRAMA ESTRUCTURAL",
  })
  cargoOrg: string;

  @Column({
  name: 'DIVISION',
    type: 'number',
    length: 22,
    precision: 4,
    comment: "DIVISION ORGANIGRAMA FUNCIONAL",
  })
  division: number;

  @Column({
  name: 'UNIDAD_DPTO',
    type: 'number',
    length: 22,
    precision: 2,
    comment: "UNIDAD (MEDICO) O DEPATRMENTO (ADMINISTRA) DEL ORGANIGRAMA FUNCIONAL",
  })
  unidadDpto: number;

  @Column({
  name: 'SERVICIO_AREA',
    type: 'number',
    length: 22,
    precision: 4,
    comment: "SERVICIO(MEDICO)  O AREA (ADMINISTRATI) ORGANIGRAMA FUNCIONAL",
  })
  servicioArea: number;

  @Column({
  name: 'ISCAID',
    type: 'number',
    length: 22,
    precision: 4,
    comment: "ESTE ES EL CODIGO DEL CARGO EN EL ORGANIGRAMA FUNCIONAL",
  })
  iscaid: number;


    @ManyToOne(
    () => SgiIsoorganigramaEntity
    )
    @JoinColumn([
      { name: 'COMPANIA', referencedColumnName: 'COMPANIA' },
      { name: 'CARGO_ORG', referencedColumnName: 'CARGO_ORG' }
    ])
    sgiIsoorganigrama: SgiIsoorganigramaEntity;

    @ManyToOne(
    () => SgiIsoareasEntity
    )
    @JoinColumn([
      { name: 'SERVICIO_AREA', referencedColumnName: 'SERVICIO_AREA' }
    ])
    sgiIsoareas: SgiIsoareasEntity;

    @ManyToOne(
    () => SsaClasificacionOcupacionesEntity
    )
    @JoinColumn([
      { name: 'CIOU', referencedColumnName: 'CIOU' }
    ])
    ssaClasificacionOcupaciones: SsaClasificacionOcupacionesEntity;

    @ManyToOne(
    () => CompaniasEntity
    )
    @JoinColumn([
      { name: 'COMPANIA', referencedColumnName: 'COMPANIA' }
    ])
    companias: CompaniasEntity;

    @ManyToOne(
    () => SgiIsodptoEntity
    )
    @JoinColumn([
      { name: 'UNIDAD_DPTO', referencedColumnName: 'UNIDAD_DPTO' }
    ])
    sgiIsodpto: SgiIsodptoEntity;

    @ManyToOne(
    () => SgiIsocargoEntity
    )
    @JoinColumn([
      { name: 'ISCAID', referencedColumnName: 'ISCAID' }
    ])
    sgiIsocargo: SgiIsocargoEntity;

    @ManyToOne(
    () => SsaPuestosSutEntity
    )
    @JoinColumn([
      { name: 'CARGOS_SUT', referencedColumnName: 'CARGOS_SUT' }
    ])
    ssaPuestosSut: SsaPuestosSutEntity;

    @ManyToOne(
    () => SgiIsodivisionEntity
    )
    @JoinColumn([
      { name: 'DIVISION', referencedColumnName: 'DIVISION' }
    ])
    sgiIsodivision: SgiIsodivisionEntity;


}
