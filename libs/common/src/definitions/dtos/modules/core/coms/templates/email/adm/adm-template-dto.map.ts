import { AdmEmailTemplate } from '@hsm-lib/definitions/enums';
import type { DtoClass } from '@hsm-lib/definitions/types';

import { AutoAdmisionTemplateDto } from './auto-admision-template.dto';
import { BienvenidaTemplateDto } from './bienvenida-template.dto';
import { CitasTurnoTemplateDto } from './citas-turno-template.dto';
import { DocumentoDerivacionAprobadoTemplateDto } from './documento-derivacion-aprovado-template.dto';
import { DocumentoDerivacionPendienteTemplateDto } from './documento-derivacion-pendiente-template.dto';
import { DocumentoDerivacionRechazadoTemplateDto } from './documento-derivacion-rechazado-template.dto';
import { ReagendamientoTemplateDto } from './reagendamiento-template.dto';
import { RecordatorioCitaTemplateDto } from './recordatorio-cita-template.dto';

export const AdmTemplateDtoMap = {
  [AdmEmailTemplate.Bienvenida]: BienvenidaTemplateDto,
  [AdmEmailTemplate.AutoAdmision]: AutoAdmisionTemplateDto,
  [AdmEmailTemplate.CitasTurno]: CitasTurnoTemplateDto,
  [AdmEmailTemplate.DocumentoDerivacionAprobado]:
    DocumentoDerivacionAprobadoTemplateDto,
  [AdmEmailTemplate.DocumentoDerivacionPendiente]:
    DocumentoDerivacionPendienteTemplateDto,
  [AdmEmailTemplate.DocumentoDerivacionRechazado]:
    DocumentoDerivacionRechazadoTemplateDto,
  [AdmEmailTemplate.Reagendamiento]: ReagendamientoTemplateDto,
  [AdmEmailTemplate.RecordatorioCita]: RecordatorioCitaTemplateDto,
} as const satisfies Record<AdmEmailTemplate, DtoClass>;
