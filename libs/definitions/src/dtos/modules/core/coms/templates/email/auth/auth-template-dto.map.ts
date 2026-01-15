import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';

import { PinInicioSesionTemplateDto } from './pin-inicio-sesion.dto';
import { PinRegistroTemplateDto } from './pin-registro.dto';
import { PinRestablecimientoContrasenaTemplateDto } from './pin-restablecimiento-constrasena.dto';
import { PinResultadoExamenTemplateDto } from './pin-resultado-examen.dto';

export const AuthTemplateDtoMap = {
  [AuthEmailTemplate.PinInicioSesion]: PinInicioSesionTemplateDto,
  [AuthEmailTemplate.PinRegistro]: PinRegistroTemplateDto,
  [AuthEmailTemplate.PinRestablecerContrasena]:
    PinRestablecimientoContrasenaTemplateDto,
  [AuthEmailTemplate.PinResultadoExamen]: PinResultadoExamenTemplateDto,
} as const;
