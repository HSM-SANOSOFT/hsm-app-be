import {
  CertificadoMedicoTemplateDto,
  ProgramacionQuirofanoTemplateDto,
  RecetaMedicaTemplateDto,
  ResultadoExamenesTemplateDto,
  SolicitudExamenesTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { HisEmailTemplate } from '@hsm-lib/definitions/enums';

export type HisTemplateDtoMap = {
  [HisEmailTemplate.CertificadoMedico]: CertificadoMedicoTemplateDto;
  [HisEmailTemplate.ProgramacionQuirofano]: ProgramacionQuirofanoTemplateDto;
  [HisEmailTemplate.RecetaMedica]: RecetaMedicaTemplateDto;
  [HisEmailTemplate.ResultadoExamenes]: ResultadoExamenesTemplateDto;
  [HisEmailTemplate.SolicitudExamenes]: SolicitudExamenesTemplateDto;
};
