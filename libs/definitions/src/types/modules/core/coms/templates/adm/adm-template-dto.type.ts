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
