import {
  Column,
  Entity,
  JoinColumn,
  ManyToOne,
  PrimaryColumn,
  Unique,
} from 'typeorm';

@Entity('SSA_PUESTOS_TRABAJO')
@Unique('UK_CARGOS_SSA', [
  'compania',
  'division',
  'unidadDpto',
  'servicioArea',
  'iscaid',
  'ciou',
  'cargosSut',
  'prSiglas',
  'cargoOrg',
])
export class SsaPuestosTrabajo {
  @PrimaryColumn({ name: 'NUMERO', type: 'number', precision: 5 })
  numero!: number;

  @Column({ name: 'COMPANIA', type: 'varchar2', length: 3 })
  compania!: string;

  @Column({ name: 'CIOU', type: 'varchar2', length: 20 })
  ciou!: string;

  @Column({ name: 'PROCESO_SUT', type: 'varchar2', length: 20 })
  procesoSut!: string;

  @Column({ name: 'PR_SIGLAS', type: 'varchar2', length: 3 })
  prSiglas!: string;

  @Column({ name: 'CARGOS_SUT', type: 'varchar2', length: 2 })
  cargosSut!: string;

  @Column({ name: 'DETALLE_ACTIVIDADES', type: 'varchar2', length: 500 })
  detalleActividades!: string;

  @Column({ name: 'OTRO_NOMBRE_CARGO', type: 'varchar2', length: 200 })
  otroNombreCargo!: string;

  @Column({ name: 'CARGO_ORG', type: 'varchar2', length: 10 })
  cargoOrg!: string;

  @Column({ name: 'DIVISION', type: 'number', precision: 4 })
  division!: number;

  @Column({ name: 'UNIDAD_DPTO', type: 'number', precision: 2 })
  unidadDpto!: number;

  @Column({ name: 'SERVICIO_AREA', type: 'number', precision: 4 })
  servicioArea!: number;

  @Column({ name: 'ISCAID', type: 'number', precision: 4 })
  iscaid!: number;

  //
  // --- RELATIONS ---
  // Wire them to the correct entity names in your project.
  //

  @ManyToOne(() => Companias)
  @JoinColumn({ name: 'COMPANIA', referencedColumnName: 'COMPANIA' })
  companiaRel!: Companias;

  @ManyToOne(() => SsaClasificacionOcupaciones)
  @JoinColumn({ name: 'CIOU', referencedColumnName: 'CIOU' })
  ciouRel!: SsaClasificacionOcupaciones;

  @ManyToOne(() => SsaPuestosSut)
  @JoinColumn({ name: 'CARGOS_SUT', referencedColumnName: 'CARGOS_SUT' })
  cargosSutRel!: SsaPuestosSut;

  @ManyToOne(() => SgiIsoDivision)
  @JoinColumn({ name: 'DIVISION', referencedColumnName: 'DIVISION' })
  divisionRel!: SgiIsoDivision;

  @ManyToOne(() => SgiIsoDpto)
  @JoinColumn({ name: 'UNIDAD_DPTO', referencedColumnName: 'UNIDAD_DPTO' })
  unidadDptoRel!: SgiIsoDpto;

  @ManyToOne(() => SgiIsoAreas)
  @JoinColumn({ name: 'SERVICIO_AREA', referencedColumnName: 'SERVICIO_AREA' })
  servicioAreaRel!: SgiIsoAreas;

  @ManyToOne(() => SgiIsoCargo)
  @JoinColumn({ name: 'ISCAID', referencedColumnName: 'ISCAID' })
  iscaidRel!: SgiIsoCargo;

  // Composite FK (COMPANIA, CARGO_ORG) → SGI_ISOORGANIGRAMA
  @ManyToOne(() => SgiIsoOrganigrama)
  @JoinColumn([
    { name: 'COMPANIA', referencedColumnName: 'COMPANIA' },
    { name: 'CARGO_ORG', referencedColumnName: 'CARGO_ORG' },
  ])
  organigramaRel!: SgiIsoOrganigrama;
}
