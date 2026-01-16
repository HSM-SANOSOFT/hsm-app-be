import { HisEmailTemplate } from '@hsm-lib/definitions/enums';
import { HisEmailRegistry } from '@hsm-lib/definitions/types';
import { CertificadoMedicoTemplate } from './certificado-medico.template';
import { ProgramacionQuirofanoTemplate } from './programacion-quirofano.template';
import { RecetaMedicaTemplate } from './receta-medica.template';
import { ResultadoExamenesTemplate } from './resultado-examen.template';
import { SolicitudExamenesTemplate } from './solicitud-examen.template';

export const hisEmailRegistry: HisEmailRegistry = {
  [HisEmailTemplate.CertificadoMedico]: {
    title: 'Certificado Médico',
    subject: 'Su Certificado Médico está listo para descargar',
    template: CertificadoMedicoTemplate,
  },
  [HisEmailTemplate.ProgramacionQuirofano]: {
    title: 'Programación de Quirófano',
    subject: 'Su programación de quirófano está lista',
    template: ProgramacionQuirofanoTemplate,
  },
  [HisEmailTemplate.RecetaMedica]: {
    title: 'Receta Médica',
    subject: 'Su Receta Médica está lista para descargar',
    template: RecetaMedicaTemplate,
  },
  [HisEmailTemplate.ResultadoExamenes]: {
    title: 'Resultado de Examenes',
    subject: 'Su Resultado de Examenes está listo para descargar',
    template: ResultadoExamenesTemplate,
  },
  [HisEmailTemplate.SolicitudExamenes]: {
    title: 'Solicitud de Exámenes',
    subject: 'Su Solicitud de Exámenes está lista para descargar',
    template: SolicitudExamenesTemplate,
  },
} satisfies HisEmailRegistry;
