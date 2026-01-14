import { HisEmailTemplate } from '@hsm-lib/definitions/enums';

export type HisTemplateDtoMap = {
  [HisEmailTemplate.CertificadoMedico]: Record<never, never>;
  [HisEmailTemplate.ProgramacionQuirofano]: Record<never, never>;
  [HisEmailTemplate.RecetaMedica]: Record<never, never>;
  [HisEmailTemplate.ResultadoExamenImagen]: Record<never, never>;
  [HisEmailTemplate.ResultadoExamenLaboratorio]: Record<never, never>;
  [HisEmailTemplate.ResultadoExamenPatologia]: Record<never, never>;
  [HisEmailTemplate.SolicitudExamenImagen]: Record<never, never>;
  [HisEmailTemplate.SolicitudExamenLaboratorio]: Record<never, never>;
  [HisEmailTemplate.SolicitudExamenPatologia]: Record<never, never>;
};
