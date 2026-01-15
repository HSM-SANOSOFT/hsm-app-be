import {
  CertificadoMedicoTemplateDto,
  ProgramacionQuirofanoTemplateDto,
  RecetaMedicaTemplateDto,
  ResultadoExamenImagenTemplateDto,
  ResultadoExamenLaboratorioTemplateDto,
  ResultadoExamenPatologiaTemplateDto,
  SolicitudExamenImagenTemplateDto,
  SolicitudExamenLaboratorioTemplateDto,
  SolicitudExamenPatologiaTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { HisEmailTemplate } from '@hsm-lib/definitions/enums';

export type HisTemplateDtoMap = {
  [HisEmailTemplate.CertificadoMedico]: CertificadoMedicoTemplateDto;
  [HisEmailTemplate.ProgramacionQuirofano]: ProgramacionQuirofanoTemplateDto;
  [HisEmailTemplate.RecetaMedica]: RecetaMedicaTemplateDto;
  [HisEmailTemplate.ResultadoExamenImagen]: ResultadoExamenImagenTemplateDto;
  [HisEmailTemplate.ResultadoExamenLaboratorio]: ResultadoExamenLaboratorioTemplateDto;
  [HisEmailTemplate.ResultadoExamenPatologia]: ResultadoExamenPatologiaTemplateDto;
  [HisEmailTemplate.SolicitudExamenImagen]: SolicitudExamenImagenTemplateDto;
  [HisEmailTemplate.SolicitudExamenLaboratorio]: SolicitudExamenLaboratorioTemplateDto;
  [HisEmailTemplate.SolicitudExamenPatologia]: SolicitudExamenPatologiaTemplateDto;
};
