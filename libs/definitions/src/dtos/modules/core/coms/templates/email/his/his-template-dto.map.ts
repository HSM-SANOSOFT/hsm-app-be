import { HisEmailTemplate } from '@hsm-lib/definitions/enums';

import { CertificadoMedicoTemplateDto } from './certificado-medico.dto';
import { ProgramacionQuirofanoTemplateDto } from './programacion-quirofano.dto';
import { RecetaMedicaTemplateDto } from './receta-medica.dto';
import { ResultadoExamenImagenTemplateDto } from './resultado-examen-imagen.dto';
import { ResultadoExamenLaboratorioTemplateDto } from './resultado-examen-laboratorio.dto';
import { ResultadoExamenPatologiaTemplateDto } from './resultado-examen-patologia.dto';
import { SolicitudExamenImagenTemplateDto } from './solucitud-examen-imagen.dto';
import { SolicitudExamenLaboratorioTemplateDto } from './solicitud-examen-laboratorio.dto';
import { SolicitudExamenPatologiaTemplateDto } from './solicitud-examen-patologia.dto';

export const HisTemplateDtoMap = {
  [HisEmailTemplate.CertificadoMedico]: CertificadoMedicoTemplateDto,
  [HisEmailTemplate.ProgramacionQuirofano]: ProgramacionQuirofanoTemplateDto,
  [HisEmailTemplate.RecetaMedica]: RecetaMedicaTemplateDto,
  [HisEmailTemplate.ResultadoExamenImagen]: ResultadoExamenImagenTemplateDto,
  [HisEmailTemplate.ResultadoExamenLaboratorio]:
    ResultadoExamenLaboratorioTemplateDto,
  [HisEmailTemplate.ResultadoExamenPatologia]:
    ResultadoExamenPatologiaTemplateDto,
  [HisEmailTemplate.SolicitudExamenImagen]: SolicitudExamenImagenTemplateDto,
  [HisEmailTemplate.SolicitudExamenLaboratorio]:
    SolicitudExamenLaboratorioTemplateDto,
  [HisEmailTemplate.SolicitudExamenPatologia]:
    SolicitudExamenPatologiaTemplateDto,
} as const;
