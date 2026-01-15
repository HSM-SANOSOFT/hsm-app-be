import { HisEmailTemplate } from '@hsm-lib/definitions/enums';
import { HisEmailRegistry } from '@hsm-lib/definitions/types';

export const hisEmailRegistry: HisEmailRegistry = {
  [HisEmailTemplate.CertificadoMedico]: {
    title: 'Certificado Médico',
    subject: 'Su Certificado Médico está listo para descargar',
    template: () => null,
  },
  [HisEmailTemplate.ProgramacionQuirofano]: {
    title: 'Programación de Quirófano',
    subject: 'Su programación de quirófano está lista',
    template: () => null,
  },
  [HisEmailTemplate.RecetaMedica]: {
    title: 'Receta Médica',
    subject: 'Su Receta Médica está lista para descargar',
    template: () => null,
  },
  [HisEmailTemplate.ResultadoExamenImagen]: {
    title: 'Resultado de Examen de Imagen',
    subject: 'Su Resultado de Examen de Imagen está listo para descargar',
    template: () => null,
  },
  [HisEmailTemplate.ResultadoExamenLaboratorio]: {
    title: 'Resultado de Examen de Laboratorio',
    subject: 'Su Resultado de Examen de Laboratorio está listo para descargar',
    template: () => null,
  },
  [HisEmailTemplate.ResultadoExamenPatologia]: {
    title: 'Resultado de Examen de Patología',
    subject: 'Su Resultado de Examen de Patología está listo para descargar',
    template: () => null,
  },
  [HisEmailTemplate.SolicitudExamenImagen]: {
    title: 'Solicitud de Examen de Imagen',
    subject: 'Su Solicitud de Examen de Imagen está lista para descargar',
    template: () => null,
  },
  [HisEmailTemplate.SolicitudExamenLaboratorio]: {
    title: 'Solicitud de Examen de Laboratorio',
    subject: 'Su Solicitud de Examen de Laboratorio está lista para descargar',
    template: () => null,
  },
  [HisEmailTemplate.SolicitudExamenPatologia]: {
    title: 'Solicitud de Examen de Patología',
    subject: 'Su Solicitud de Examen de Patología está lista para descargar',
    template: () => null,
  },
} satisfies HisEmailRegistry;
