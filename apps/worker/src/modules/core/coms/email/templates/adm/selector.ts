import {
  AutoAdmisionTemplateDto,
  BienvenidaTemplateDto,
  CitasTurnoTemplateDto,
  DocumentoDerivacionAprobadoTemplateDto,
  DocumentoDerivacionPendienteTemplateDto,
  DocumentoDerivacionRechazadoTemplateDto,
  ReagendamientoTemplateDto,
  RecordatorioCitaTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { AdmEmailTemplate } from '@hsm-lib/definitions/enums';
import { EmailTemplateSelector } from '@hsm-lib/definitions/types';
import { AutoAdmisionTemplate } from './auto-admision.template';
import { BienvenidaTemplate } from './bienvenida.template';
import { CitasTurnoTemplate } from './citas-turno.template';
import { DocumentoDerivacionAprobadoTemplate } from './documento-derivacion-aprovado.template';
import { DocumentoDerivacionPendienteTemplate } from './documento-derivacion-pendiente.template';
import { DocumentoDerivacionRechazadoTemplate } from './documento-derivacion-rechazado.template';
import { ReagendamientoTemplate } from './reagendamiento.template';
import { RecordatorioCitaTemplate } from './recordatorio-cita.template';

export type AdmTemplateDtoMap = {
  [AdmEmailTemplate.Bienvenida]: BienvenidaTemplateDto;
  [AdmEmailTemplate.AutoAdmision]: AutoAdmisionTemplateDto;
  [AdmEmailTemplate.CitasTurno]: CitasTurnoTemplateDto;
  [AdmEmailTemplate.DocumentoDerivacionAprobado]: DocumentoDerivacionAprobadoTemplateDto;
  [AdmEmailTemplate.DocumentoDerivacionPendiente]: DocumentoDerivacionPendienteTemplateDto;
  [AdmEmailTemplate.DocumentoDerivacionRechazado]: DocumentoDerivacionRechazadoTemplateDto;
  [AdmEmailTemplate.Reagendamiento]: ReagendamientoTemplateDto;
  [AdmEmailTemplate.RecordatorioCita]: RecordatorioCitaTemplateDto;
};

export type AdmEmailRegistry = {
  [K in AdmEmailTemplate]: EmailTemplateSelector<AdmTemplateDtoMap[K]>;
};

export const ADM_EMAIL_REGISTRY: AdmEmailRegistry = {
  [AdmEmailTemplate.Bienvenida]: {
    subject: 'Bienvenido al Hospital',
    template: BienvenidaTemplate,
  },
  [AdmEmailTemplate.AutoAdmision]: {
    subject: 'Confirmación de Auto Admisión',
    template: AutoAdmisionTemplate,
  },
  [AdmEmailTemplate.CitasTurno]: {
    subject: 'Confirmación de Cita / Turno',
    template: CitasTurnoTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionAprobado]: {
    subject: 'Documento de Derivación Aprobado',
    template: DocumentoDerivacionAprobadoTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionPendiente]: {
    subject: 'Documento de Derivación Pendiente',
    template: DocumentoDerivacionPendienteTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionRechazado]: {
    subject: 'Documento de Derivación Rechazado',
    template: DocumentoDerivacionRechazadoTemplate,
  },
  [AdmEmailTemplate.Reagendamiento]: {
    subject: 'Reagendamiento de Cita / Turno',
    template: ReagendamientoTemplate,
  },
  [AdmEmailTemplate.RecordatorioCita]: {
    subject: 'Recordatorio de Cita / Turno',
    template: RecordatorioCitaTemplate,
  },
} satisfies AdmEmailRegistry;
