import {
  AdmServidoresDiagnosticoEntity,
  EspecialidadesEntity,
  EspecialidadesMedicosEntity,
  HorariosMedicoEntity,
  PersonalEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import { Injectable, Logger } from '@nestjs/common';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import { DataSource, Repository, UpdateResult } from 'typeorm';
@Injectable()
export class AvailabilityService {
  private readonly logger = new Logger(AvailabilityService.name);

  constructor(
    @InjectDataSource(Databases.HsmDbOracle)
    private readonly dataSourceOracle: DataSource,
    @InjectDataSource(Databases.HsmDbPostgres)
    private readonly dataSourcePostgres: DataSource,
    @InjectRepository(PersonalEntity, Databases.HsmDbOracle)
    private readonly personalRepository: Repository<PersonalEntity>,
    @InjectRepository(EspecialidadesEntity, Databases.HsmDbOracle)
    private readonly especialidadesRepository: Repository<EspecialidadesEntity>,
    @InjectRepository(EspecialidadesMedicosEntity, Databases.HsmDbOracle)
    private readonly especialidadesMedicosRepository: Repository<EspecialidadesMedicosEntity>,
    @InjectRepository(HorariosMedicoEntity, Databases.HsmDbOracle)
    private readonly horariosMedicoRepository: Repository<HorariosMedicoEntity>,
    @InjectRepository(AdmServidoresDiagnosticoEntity, Databases.HsmDbOracle)
    private readonly admServidoresDiagnosticoRepository: Repository<AdmServidoresDiagnosticoEntity>,
  ) {}

  async getSpecialties() {
    const rows = await this.especialidadesMedicosRepository
      .createQueryBuilder('esp')
      .select('DISTINCT e.especialidad', 'ESPECIALIDAD')
      .leftJoin(EspecialidadesEntity, 'e', 'esp.espCodigo = e.codigo')
      .leftJoin(PersonalEntity, 'p', 'esp.prsCodigo = p.codigo')
      .leftJoin(HorariosMedicoEntity, 'h', 'h.prsCodigo = p.codigo')
      .leftJoin(AdmServidoresDiagnosticoEntity, 'sd', 'sd.prsCodigo = p.codigo')
      .where('p.estadoDeDisponibilidad = :disp', { disp: 'D' })
      .andWhere('e.estadoDeDisponibilidad = :disp', { disp: 'D' })
      .andWhere('p.permitirTurno = :permitirTurno', { permitirTurno: 'V' })
      .andWhere('h.tipoTurno IS NOT NULL')
      .andWhere('sd.prsCodigo IS NULL')
      .andWhere('h.tipoTurno = :tipoTurno', { tipoTurno: 'P' })
      .andWhere('e.nivelConsulta < :nivel', { nivel: 4 })
      .orderBy('e.especialidad', 'ASC')
      .getRawMany<{ ESPECIALIDAD: string }>();
    return rows.map(r => r.ESPECIALIDAD);
  }

  getProvidersBySpecialty(params: { specialtyId: string }) {
    return { params };
  }

  getProviderDates(
    params: { providerId: string },
    query: {
      specialtyId?: string;
      dateFrom?: string;
      dateTo?: string;
    },
  ) {
    return { params, query };
  }

  getProviderSlots(
    params: { providerId: string; date: string },
    query: { specialtyId?: string },
  ) {
    return { params, query };
  }
}
