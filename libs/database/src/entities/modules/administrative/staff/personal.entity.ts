import {
  AreasEntity,
  DepartamentosEntity,
  EspecialidadPersonalEntity,
  SsaPuestosTrabajoEntity,
} from '@hsm-lib/database/entities';
import {
  Column,
  Entity,
  Index,
  JoinColumn,
  ManyToOne,
  PrimaryColumn,
} from 'typeorm';

@Entity({
  name: 'PERSONAL',
})
export class PersonalEntity {
  @PrimaryColumn({
    name: 'CODIGO',
    length: 4,
    primaryKeyConstraintName: 'PRS_PK',
    comment: 'Código del personal',
  })
  codigo!: string;

  @Index('PRS_ESPPRS_FK_I')
  @Column({
    name: 'ESPPRS_CODIGO',
    length: 3,
    comment: 'Código del cargo',
  })
  espprsCodigo!: string;

  @Column({
    name: 'APELLIDOS',
    length: 30,
    comment: 'Apellidos',
  })
  apellidos!: string;

  @Column({
    name: 'NOMBRES',
    length: 25,
    comment: 'Nombres',
  })
  nombres!: string;

  @Column({
    name: 'ESTADO_DE_DISPONIBILIDAD',
    length: 1,
    comment: 'Si esta disponible, fuera de servicio, etc',
    default: 'D',
  })
  estadoDeDisponibilidad!: string;

  @Column({
    name: 'CEDULA',
    length: 10,
    comment: 'Cédula de identidad',
  })
  cedula!: string;

  @Column({
    name: 'CARGO',
    length: 3,
    comment: 'Cargo del personal',
  })
  cargo!: string;

  @Column({
    name: 'TELEFONO',
    length: 24,
    nullable: true,
    comment: 'Teléfonos del médico',
  })
  telefono!: string | null;

  @Column({
    name: 'DIRECCION',
    length: 200,
    nullable: true,
    comment: 'Dirección domiciliaria',
  })
  direccion!: string | null;

  @Column({
    name: 'NUMERO_CMA',
    length: 5,
    nullable: true,
    comment: 'Num. Colegio Médico',
  })
  numeroCma!: string | null;

  @Column({
    name: 'USUARIO',
    length: 30,
    unique: true,
    comment: 'Nombre de usuario de la BD',
  })
  usuario!: string;

  @Column({
    name: 'PERMITIR_TURNO',
    length: 1,
    comment: 'Permitir turno',
    default: 'F',
  })
  permitirTurno!: string;

  @Column({
    name: 'PERSONAL_CIRUGIA',
    length: 1,
    comment: 'Indicador personal cirugía',
    default: 'F',
  })
  personalCirugia!: string;

  @Column({
    name: 'BENEFICIARIO',
    length: 1,
    comment: 'Beneficiario',
    default: 'F',
  })
  beneficiario!: string;

  @Column({
    name: 'LIBRO_MSP',
    length: 20,
    nullable: true,
    comment: 'POR ELIMINAR',
  })
  libroMsp!: string | null;

  @Column({
    name: 'FOLIO_MSP',
    length: 20,
    nullable: true,
    comment: 'POR ELIMINAR',
  })
  folioMsp!: string | null;

  @Column({
    name: 'NUMERO_MSP',
    length: 20,
    nullable: true,
    comment: 'POR ELIMINAR',
  })
  numeroMsp!: string | null;

  @Column({
    name: 'AREA_FISICA_ASIGNADA',
    length: 1,
    comment: 'Código de área física',
  })
  areaFisicaAsignada!: string;

  @Column({
    name: 'DEPARTAMENTO_FISICO_ASIGNADO',
    length: 2,
    comment: 'Código del Departamento',
  })
  departamentoFisicoAsignado!: string;

  @Column({
    name: 'EMAIL',
    length: 360,
    comment: 'Mail del médico',
  })
  email!: string;

  @Column({
    name: 'FIRMA_INICIALES',
    type: 'blob',
    nullable: true,
    comment: 'El visto bueno',
  })
  firmaIniciales!: Buffer | null;

  @Column({
    name: 'FIRMA_RUBRICA',
    type: 'long',
    nullable: true,
  })
  firmaRubrica!: string | null;

  @Column({
    name: 'SELLO',
    type: 'blob',
    nullable: true,
  })
  sello!: Buffer | null;

  @Column({
    name: 'FIRMA_Y_SELLO',
    type: 'blob',
    nullable: true,
  })
  firmaYSello!: Buffer | null;

  @Column({
    name: 'FIRMA_Y_SELLO_H',
    type: 'blob',
    nullable: true,
  })
  firmaYSelloH!: Buffer | null;

  @Column({
    name: 'SEXO',
    length: 1,
    comment: 'Sexo',
  })
  sexo!: string;

  @Column({
    name: 'SENESCYT',
    length: 20,
    nullable: true,
  })
  senescyt!: string | null;

  @Column({
    name: 'MIEMBRO_STAFF',
    length: 1,
    comment: 'Miembro del staff',
  })
  miembroStaff!: string;

  @Column({
    name: 'PASS_CERT',
    length: 20,
    nullable: true,
  })
  passCert!: string | null;

  @Column({
    name: 'UBICACION',
    length: 3,
    nullable: true,
    comment: 'Cubículo donde atiende',
  })
  ubicacion!: string | null;

  @Column({
    name: 'ATENCION_HOSPITALARIA',
    length: 1,
    comment: 'Atención hospitalaria',
  })
  atencionHospitalaria!: string;

  @Column({
    name: 'PASSWORD_HASH',
    type: 'clob',
    nullable: true,
    comment: 'Hash de password para migración',
  })
  passwordHash!: string | null;

  @Column({
    name: 'NOMINA',
    length: 1,
    comment: 'En nómina',
  })
  nomina!: string;

  @Column({
    name: 'NUMERO_SSA',
    type: 'number',
    comment: 'Puesto SSA',
  })
  numeroSsa!: number;

  @Column({
    name: 'FECHA',
    type: 'date',
    nullable: true,
    comment: 'Fecha creación usuario',
  })
  fecha!: Date | null;

  @Column({
    name: 'CLASE_MEDICO',
    length: 1,
    nullable: true,
  })
  claseMedico!: string | null;

  @ManyToOne(() => EspecialidadPersonalEntity)
  @JoinColumn({
    name: 'ESPPRS_CODIGO',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'PRS_ESPPRS_FK',
  })
  especialidadPersonal!: EspecialidadPersonalEntity;

  @ManyToOne(() => AreasEntity)
  @JoinColumn({
    name: 'AREA_FISICA_ASIGNADA',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'PRS_AREA_FK',
  })
  area!: AreasEntity;

  @ManyToOne(() => SsaPuestosTrabajoEntity)
  @JoinColumn({
    name: 'NUMERO_SSA',
    referencedColumnName: 'codigo',
    foreignKeyConstraintName: 'PRS_SSA_FK',
  })
  puestoSsa!: SsaPuestosTrabajoEntity;

  @ManyToOne(() => DepartamentosEntity)
  @JoinColumn([
    {
      name: 'AREA_FISICA_ASIGNADA',
      referencedColumnName: 'areaCodigo',
    },
    {
      name: 'DEPARTAMENTO_FISICO_ASIGNADO',
      referencedColumnName: 'departamentoCodigo',
    },
  ])
  departamento!: DepartamentosEntity;
}
