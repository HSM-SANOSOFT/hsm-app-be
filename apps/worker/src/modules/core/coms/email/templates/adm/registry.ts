import { AdmEmailTemplate } from '@hsm-lib/definitions/enums';
import { AdmEmailRegistry } from '@hsm-lib/definitions/types';
import { AutoAdmisionTemplate } from './auto-admision.template';
import { BienvenidaTemplate } from './bienvenida.template';
import { CitasTurnoTemplate } from './citas-turno.template';
import { DocumentoDerivacionAprobadoTemplate } from './documento-derivacion-aprovado.template';
import { DocumentoDerivacionPendienteTemplate } from './documento-derivacion-pendiente.template';
import { DocumentoDerivacionRechazadoTemplate } from './documento-derivacion-rechazado.template';
import { ReagendamientoTemplate } from './reagendamiento.template';
import { RecordatorioCitaTemplate } from './recordatorio-cita.template';

export const admEmailRegistry: AdmEmailRegistry = {
  [AdmEmailTemplate.Bienvenida]: {
    title: 'Bienvenido al Hospital',
    subject: 'Bienvenido al Hospital',
    template: BienvenidaTemplate,
  },
  [AdmEmailTemplate.AutoAdmision]: {
    title: 'Confirmación de Auto Admisión',
    subject: 'Confirmación de Auto Admisión',
    template: AutoAdmisionTemplate,
  },
  [AdmEmailTemplate.CitasTurno]: {
    title: 'Confirmación de Cita / Turno',
    subject: 'Confirmación de Cita / Turno',
    template: CitasTurnoTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionAprobado]: {
    title: 'Documento de Derivación Aprobado',
    subject: 'Documento de Derivación Aprobado',
    template: DocumentoDerivacionAprobadoTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionPendiente]: {
    title: 'Documento de Derivación Pendiente',
    subject: 'Documento de Derivación Pendiente',
    template: DocumentoDerivacionPendienteTemplate,
  },
  [AdmEmailTemplate.DocumentoDerivacionRechazado]: {
    title: 'Documento de Derivación Rechazado',
    subject: 'Documento de Derivación Rechazado',
    template: DocumentoDerivacionRechazadoTemplate,
  },
  [AdmEmailTemplate.Reagendamiento]: {
    title: 'Reagendamiento de Cita / Turno',
    subject: 'Reagendamiento de Cita / Turno',
    template: ReagendamientoTemplate,
  },
  [AdmEmailTemplate.RecordatorioCita]: {
    title: 'Recordatorio de Cita / Turno',
    subject: 'Recordatorio de Cita / Turno',
    template: RecordatorioCitaTemplate,
  },
} satisfies AdmEmailRegistry;
