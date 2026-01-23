import { HisEmailTemplate } from '@hsm-lib/definitions/enums';
import type { DtoClass } from '@hsm-lib/definitions/types';

import { CertificadoMedicoTemplateDto } from './certificado-medico.dto';
import { ProgramacionQuirofanoTemplateDto } from './programacion-quirofano.dto';
import { RecetaMedicaTemplateDto } from './receta-medica.dto';
import { ResultadoExamenesTemplateDto } from './resultado-examen.dto';
import { SolicitudExamenesTemplateDto } from './solicitud-examen.dto';

export const HisTemplateDtoMap = {
  [HisEmailTemplate.CertificadoMedico]: CertificadoMedicoTemplateDto,
  [HisEmailTemplate.ProgramacionQuirofano]: ProgramacionQuirofanoTemplateDto,
  [HisEmailTemplate.RecetaMedica]: RecetaMedicaTemplateDto,
  [HisEmailTemplate.ResultadoExamenes]: ResultadoExamenesTemplateDto,
  [HisEmailTemplate.SolicitudExamenes]: SolicitudExamenesTemplateDto,
} as const satisfies Record<HisEmailTemplate, DtoClass>;
