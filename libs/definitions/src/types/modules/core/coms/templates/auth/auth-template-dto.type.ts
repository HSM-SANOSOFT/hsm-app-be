import {
  PinInicioSesionTemplateDto,
  PinRegistroTemplateDto,
  PinRestablecimientoContrasenaTemplateDto,
  PinResultadoExamenTemplateDto,
} from '@hsm-lib/definitions/dtos';
import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';

export type AuthTemplateDtoMap = {
  [AuthEmailTemplate.PinInicioSesion]: PinInicioSesionTemplateDto;
  [AuthEmailTemplate.PinRegistro]: PinRegistroTemplateDto;
  [AuthEmailTemplate.PinRestablecerContrasena]: PinRestablecimientoContrasenaTemplateDto;
  [AuthEmailTemplate.PinResultadoExamen]: PinResultadoExamenTemplateDto;
};
