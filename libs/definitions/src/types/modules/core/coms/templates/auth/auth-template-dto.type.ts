import { AuthEmailTemplate } from '@hsm-lib/definitions/enums';

export type AuthTemplateDtoMap = {
  [AuthEmailTemplate.PinInicioSesion]: Record<never, never>;
  [AuthEmailTemplate.PinRegistro]: Record<never, never>;
  [AuthEmailTemplate.PinRestablecerContrasena]: Record<never, never>;
  [AuthEmailTemplate.PinResultadoExamen]: Record<never, never>;
};
