import { parseIsoLocalDate, startOfLocalDay } from '@hsm-lib/common';
import {
  AdmServidoresDiagnosticoEntity,
  EspecialidadesEntity,
  EspecialidadesMedicosEntity,
  HorariosMedicoEntity,
  PersonalEntity,
} from '@hsm-lib/database/entities';
import { Databases } from '@hsm-lib/database/sources';
import { AppointmentType } from '@hsm-lib/definitions/enums';
import { Injectable, Logger } from '@nestjs/common';
import { InjectDataSource, InjectRepository } from '@nestjs/typeorm';
import { DataSource, Repository } from 'typeorm';

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
      .select('e.especialidad', 'ESPECIALIDAD')
      .addSelect('e.codigo', 'CODIGO')
      .distinct(true)
      .addSelect('e.codigo', 'CODIGO')
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
      .getRawMany<{ ESPECIALIDAD: string; CODIGO: string }>();
    return rows.map(r => {
      const especialidadesMap: {
        original: string;
        mapped: string;
      }[] = [
        {
          original: 'Traumatología y Ort',
          mapped: 'Traumatología',
        },
        { original: 'Otorrinolaringología', mapped: 'Otorrino' },
        { original: 'Cardiologo Pediatrico', mapped: 'Cardiologo Ped' },
      ];
      const original = r.ESPECIALIDAD;
      const found = especialidadesMap.find(item => item.original === original);
      const mapped = found ? found.mapped : original;
      const name = mapped.length > 20 ? mapped.slice(0, 20) : mapped;
      return { specialtyId: r.CODIGO, name };
    });
  }

  async getProvidersBySpecialty(params: { specialtyId: string }) {
    const { specialtyId } = params;
    const rows = await this.especialidadesMedicosRepository
      .createQueryBuilder('esp')
      .select(`REGEXP_SUBSTR(p.NOMBRES, '[^ ]+', 1, 1)`, 'FIRST_NAME')
      .addSelect(`REGEXP_SUBSTR(p.NOMBRES, '[^ ]+', 1, 2)`, 'SECOND_NAME')
      .addSelect(`REGEXP_SUBSTR(p.APELLIDOS, '[^ ]+', 1, 1)`, 'FIRST_LASTNAME')
      .addSelect(`REGEXP_SUBSTR(p.APELLIDOS, '[^ ]+', 1, 2)`, 'SECOND_LASTNAME')
      .addSelect('p.SEXO', 'GENDER')
      .addSelect('p.CODIGO', 'PROVIDER_CODE')
      .distinct(true)
      .leftJoin(EspecialidadesEntity, 'e', 'esp.ESP_CODIGO = e.CODIGO')
      .leftJoin(PersonalEntity, 'p', 'esp.PRS_CODIGO = p.CODIGO')
      .leftJoin(HorariosMedicoEntity, 'h', 'h.PRS_CODIGO = p.CODIGO')
      .leftJoin(
        AdmServidoresDiagnosticoEntity,
        'sd',
        'sd.PRS_CODIGO = p.CODIGO',
      )
      .where('p.ESTADO_DE_DISPONIBILIDAD = :disp', { disp: 'D' })
      .andWhere('e.ESTADO_DE_DISPONIBILIDAD = :disp', { disp: 'D' })
      .andWhere('p.PERMITIR_TURNO = :permitirTurno', { permitirTurno: 'V' })
      .andWhere('h.TIPO_TURNO IS NOT NULL')
      .andWhere('sd.PRS_CODIGO IS NULL')
      .andWhere('h.TIPO_TURNO = :tipoTurno', { tipoTurno: 'P' })
      .andWhere('e.CODIGO = :specialtyId', { specialtyId })
      .orderBy('FIRST_NAME', 'ASC')
      .getRawMany<{
        FIRST_NAME: string;
        SECOND_NAME: string;
        FIRST_LASTNAME: string;
        SECOND_LASTNAME: string;
        GENDER: string;
        PROVIDER_CODE: string;
      }>();

    return rows.map(r => ({
      providerId: r.PROVIDER_CODE,
      firstName: r.FIRST_NAME,
      secondName: r.SECOND_NAME,
      firstLastName: r.FIRST_LASTNAME,
      secondLastName: r.SECOND_LASTNAME,
      gender: r.GENDER,
    }));
  }

  async getProviderDates(
    params: { providerId: string },
    query: {
      dateFrom?: string;
      dateTo?: string;
      appointmentType?: AppointmentType;
    },
  ) {
    const { providerId } = params;
    const { dateFrom, dateTo } = query;
    const maxDayCount = 60;

    const today = startOfLocalDay(new Date());

    const fromDate = dateFrom ? parseIsoLocalDate(dateFrom) : today;
    const toDate = dateTo
      ? parseIsoLocalDate(dateTo)
      : new Date(today.getTime() + 30 * 24 * 60 * 60 * 1000);

    const msPerDay = 24 * 60 * 60 * 1000;
    let dayCount = Math.floor(
      (toDate.getTime() - fromDate.getTime()) / msPerDay,
    );
    if (dayCount < 0) {
      return [];
    }

    if (dayCount > maxDayCount) {
      dayCount = maxDayCount;
    }

    const result: { date: string; slots: string[] }[] = [];

    for (let i = 0; i <= dayCount; i++) {
      const current = new Date(fromDate.getTime() + i * msPerDay);

      const dd = String(current.getDate()).padStart(2, '0');
      const mm = String(current.getMonth() + 1).padStart(2, '0');
      const yyyy = current.getFullYear();

      const fechaStr = `${dd}/${mm}/${yyyy}`;

      const appointmentType = query.appointmentType || AppointmentType.Privado;

      const horarioRows = await this.dataSourceOracle.query(
        `
      SELECT
        BUSCA_HORARIO_MEDICO(:1, :2, :3, 1) AS START_TIME,
        BUSCA_HORARIO_MEDICO(:1, :2, :3, 2) AS END_TIME,
        BUSCA_HORARIO_MEDICO(:1, :2, :3, 3) AS SLOT_MINUTES
      FROM DUAL
      `,
        [providerId, fechaStr, appointmentType],
      );

      const horario = horarioRows[0] as {
        START_TIME: string | null;
        END_TIME: string | null;
        SLOT_MINUTES: string | null;
      };

      if (
        !horario ||
        !horario.START_TIME ||
        !horario.END_TIME ||
        !horario.SLOT_MINUTES
      ) {
        continue;
      }

      const slotMinutes = parseInt(horario.SLOT_MINUTES, 10);
      if (!slotMinutes || slotMinutes <= 0) {
        continue;
      }

      const parseOracleDateTime = (s: string): Date => {
        const [datePart, timePart] = s.split(' ');
        const [y, m, d] = datePart.split('-').map(Number);
        const [hh, mi] = timePart.split(':').map(Number);
        return new Date(y, m - 1, d, hh, mi, 0);
      };

      const start = parseOracleDateTime(horario.START_TIME);
      const end = parseOracleDateTime(horario.END_TIME);

      const candidateSlots: string[] = [];
      let currentSlot = new Date(start.getTime());

      while (currentSlot <= end) {
        const h = String(currentSlot.getHours()).padStart(2, '0');
        const m = String(currentSlot.getMinutes()).padStart(2, '0');
        candidateSlots.push(`${h}:${m}`);
        currentSlot = new Date(currentSlot.getTime() + slotMinutes * 60 * 1000);
      }

      if (!candidateSlots.length) {
        continue;
      }

      const bookedRows = await this.dataSourceOracle.query(
        `
      SELECT TO_CHAR(T.CREADO, 'HH24:MI') AS HORA
      FROM TURNOS_CE T
      WHERE TRUNC(T.CREADO) = TO_DATE(:2, 'DD/MM/YYYY')
        AND T.PRS_CODIGO = :1
        AND T.ESTADO IN ('P', 'R', 'A')
      `,
        [providerId, fechaStr],
      );

      const bookedSet = new Set<string>(
        (bookedRows as { HORA: string }[]).map(r => r.HORA),
      );

      const freeSlots = candidateSlots.filter(slot => !bookedSet.has(slot));

      if (!freeSlots.length) {
        continue;
      }

      const isoDate = `${yyyy}-${mm}-${dd}`;
      result.push({
        date: isoDate,
        slots: freeSlots,
      });
    }

    return result;
  }
}
